using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
#if STEAMRELEASE
using Steamworks;
#endif
using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;
using VRCVideoCacher.API;
using VRCVideoCacher.Database;
using VRCVideoCacher.Elevator;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher;

internal sealed class Program
{
    public static string YtdlpHash = string.Empty;
    public const string Version = "2026.3.8";
    public static readonly ILogger Logger = Log.ForContext("SourceContext", "Core");
    public static readonly string CurrentProcessPath = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
    public static readonly string DataPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCVideoCacher");
    public static event Action? OnCookiesUpdated;

    [STAThread]
    public static void Main(string[] args)
    {
        // Must run before Steam API init — this process may be a privileged subprocess invoked by ElevatorManager
        HostsManager.TryRun();

#if STEAMRELEASE
        if (SteamAPI.RestartAppIfNecessary(new AppId_t(4296960)))
        {
            Environment.Exit(0);
            return;
        }

        if (!SteamAPI.Init())
        {
            Console.Error.WriteLine("SteamAPI.Init() failed. Make sure Steam is running.");
            Environment.Exit(1);
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => SteamAPI.Shutdown();
#endif
        AdminCheck.SetupArguements(args);

        var processes = Process.GetProcessesByName("VRCVideoCacher");
        if (processes.Length > 1)
        {
            Console.WriteLine("Application is already running, Exiting...");
            Environment.Exit(0);
        }
        foreach (var process in processes)
            process.Dispose();

        // Check for --nogui flag
        if (args.Contains("--nogui"))
        {
            // Run backend only (console mode)
            InitVRCVideoCacher(false).GetAwaiter().GetResult();
            return;
        }

        // Configure Serilog with UI sink
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(new ExpressionTemplate(
                "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n\r{@x}",
                theme: TemplateTheme.Literate))
            .WriteTo.File(
                path: "logs/VRCVideoCacher.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 5)
            .WriteTo.Sink(new UiLogSink())
            .CreateLogger();

        if (AdminCheck.IsRunningAsAdmin())
        {
            Logger.Warning("Application is running with administrator privileges. This is not recommended for security reasons.");
        }

        // Don't run backend if admin warning is shown
        if (!AdminCheck.ShouldShowAdminWarning())
        {
            // Start backend on background thread
            Task.Run(async () =>
            {
                try
                {
                    await InitVRCVideoCacher(true);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Backend error " + ex.Message + " " + ex.StackTrace);
                }
            });
        }

        // Start the UI
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static async Task InitVRCVideoCacher(bool hasGui)
    {
        try { Console.Title = $"VRCVideoCacher v{Version}{AdminCheck.GetAdminTitleWarning()}"; } catch { /* GUI mode, no console */ }

        // Only configure logger if not already configured (e.g., by UI)
        if (Log.Logger.GetType().Name == "SilentLogger")
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(new ExpressionTemplate(
                    "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n{@x}",
                    theme: TemplateTheme.Literate))
                .CreateLogger();
        }
        const string elly = "Elly";
        const string natsumi = "Natsumi";
        const string haxy = "Haxy";
        Logger.Information("VRCVideoCacher version {Version} created by {Elly}, {Natsumi}, {Haxy}", Version, elly, natsumi, haxy);

        if (!hasGui && AdminCheck.ShouldShowAdminWarning())
        {
            Logger.Error(AdminCheck.AdminWarningMessage);
            Environment.Exit(0);
        }

        Directory.CreateDirectory(DataPath);
        await Updater.CheckForUpdates();
        Updater.Cleanup();
        if (Environment.CommandLine.Contains("--Reset"))
        {
            FileTools.RestoreAllYtdl();
            Environment.Exit(0);
        }
        if (Environment.CommandLine.Contains("--Hash"))
        {
            Console.WriteLine(GetOurYtdlpHash());
            Environment.Exit(0);
        }
        Console.CancelKeyPress += (_, _) => Environment.Exit(0);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => OnAppQuit();

        YtdlpHash = GetOurYtdlpHash();
        await VvcConfigService.GetConfig();
        if (ConfigManager.Config.YtdlpAutoUpdate && !string.IsNullOrEmpty(ConfigManager.Config.YtdlpPath))
        {
            await YtdlManager.TryDownloadYtdlp();
            YtdlManager.StartYtdlDownloadThread();
            _ = YtdlManager.TryDownloadDeno();
            _ = YtdlManager.TryDownloadFfmpeg();
        }

        if (OperatingSystem.IsWindows())
            AutoStartShortcut.TryUpdateShortcutPath();
        DatabaseManager.Init();
        WebServer.Init();
        FileTools.BackupAllYtdl();
        await BulkPreCache.DownloadFileList();

        if (ConfigManager.Config.YtdlpUseCookies && !IsCookiesEnabledAndValid())
            Logger.Warning("No cookies found, please use the browser extension to send cookies or disable \"ytdlUseCookies\" in config.");

        CacheManager.Init();

        // run after init to avoid text spam blocking user input
        if (OperatingSystem.IsWindows())
            _ = WinGet.TryInstallPackages();

        if (YtdlManager.GlobalYtdlConfigExists())
            Logger.Error("Global yt-dlp config file found in \"%AppData%\\yt-dlp\". Please delete it to avoid conflicts with VRCVideoCacher.");

        await Task.Delay(-1);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    public static bool IsCookiesEnabledAndValid()
    {
        if (!ConfigManager.Config.YtdlpUseCookies)
            return false;

        if (!File.Exists(YtdlManager.CookiesPath))
            return false;

        var cookies = File.ReadAllText(YtdlManager.CookiesPath);
        return IsCookiesValid(cookies);
    }

    public static bool IsCookiesValid(string cookies)
    {
        if (string.IsNullOrEmpty(cookies))
            return false;

        if (cookies.Contains("youtube.com") && cookies.Contains("LOGIN_INFO"))
            return true;

        return false;
    }
    
    public static async Task<bool?> ValidateCookiesAsync()
    {
        if (!IsCookiesEnabledAndValid())
            return null;

        try
        {
            var cookieContainer = new CookieContainer();
            var lines = await File.ReadAllLinesAsync(YtdlManager.CookiesPath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                    continue;

                var parts = line.Split('\t');
                if (parts.Length < 7)
                    continue;

                try
                {
                    var domain = parts[0];
                    var path = parts[2];
                    var secure = parts[3].Equals("TRUE", StringComparison.OrdinalIgnoreCase);
                    var name = parts[5];
                    var value = parts[6];

                    cookieContainer.Add(new Cookie(name, value, path, domain) { Secure = secure });
                }
                catch
                {
                    // Skip malformed cookie lines
                }
            }

            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                CookieContainer = cookieContainer,
                UseCookies = true
            };
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await client.GetAsync("https://www.youtube.com/account", cts.Token);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            Logger.Warning("Failed to validate cookies online: {Error}", ex.Message);
            return null;
        }
    }

    public static Stream GetYtDlpStub()
    {
        return GetEmbeddedResource("VRCVideoCacher.yt-dlp-stub.exe");
    }

    public static Stream GetEmbeddedResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new Exception($"{resourceName} not found in resources.");

        return stream;
    }

    private static string GetOurYtdlpHash()
    {
        var stream = GetYtDlpStub();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        stream.Dispose();
        return ComputeBinaryContentHash(ms.ToArray());
    }

    public static string ComputeBinaryContentHash(byte[] base64)
    {
        return Convert.ToBase64String(SHA256.HashData(base64));
    }

    private static void OnAppQuit()
    {
        FileTools.RestoreAllYtdl();
        Logger.Information("Exiting...");
    }

    public static void NotifyCookiesUpdated()
    {
        OnCookiesUpdated?.Invoke();
    }
}
