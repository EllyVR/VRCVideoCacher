using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
#if STEAMRELEASE
using Steamworks;
#endif
using Serilog;
using Serilog.Events;
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
    // Versioning is YEAR.MONTH.RELEASE
    public const string Version = "2026.3.11";
    public const string Creator_Elly = "Elly";
    public const string Creator_Natsumi = "Natsumi";
    public const string Creator_Haxy = "Haxy";
    public const string Creator_Hauskaz = "Hauskaz";
    public const string Creator_DubyaDude = "DubyaDude";
    public const string Sentry_DSN = "https://233e3c027a6239500a4bb3ba81f99ddd@sentry.ellyvr.dev/19";
    public static ILogger Logger = Log.ForContext("SourceContext", "Core");
    public static readonly string CurrentProcessPath = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
    public static readonly string DataPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCVideoCacher");
    public static readonly string UtilsPath = Path.Join(DataPath, "Utils");
    public static readonly string LogsPath = Path.Join(DataPath, "Logs");
    public static event Action? OnCookiesUpdated;

    [STAThread]
    public static void Main(string[] args)
    {
        // Crash reporting
        SentrySdk.Init(options =>
        {
            options.Dsn = Sentry_DSN;
            options.AutoSessionTracking = true;
            options.IsGlobalModeEnabled = true;
            options.Release = Version;
            var platform = OperatingSystem.IsLinux() ? "linux" : "windows";
#if STEAMRELEASE
            options.Environment = $"steam-{platform}";
#else
            options.Environment = platform;
#endif
        });
        
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
        var processes = Process.GetProcessesByName("VRCVideoCacher");
        if (processes.Length > 1)
        {
            Console.WriteLine("Application is already running, Exiting...");
            Environment.Exit(0);
        }
        foreach (var process in processes)
            process.Dispose();

        LaunchArgs.SetupArguments(args);
        InitializeLogger();

#if !DEBUG
        AppDomain.CurrentDomain.UnhandledException += async(sender, e) =>
        {
            SentrySdk.ConfigureScope(scope =>
            {
                var configPath = Path.Join(DataPath, "Config.json");
                if (File.Exists(configPath))
                    scope.AddAttachment(configPath);
            });

            if (e.ExceptionObject is Exception ex0) SentrySdk.CaptureException(ex0);
            try
            {
                var ex = e.ExceptionObject as Exception;
                Logger.Error(ex, "Unhandled Exception");
            }
            catch
            {
                try
                {
                    var ex = e.ExceptionObject as Exception;
                    Console.WriteLine("Unhandled Exception: " + ex?.ToString());
                }
                catch
                {
                    // If logging fails, there's not much we can do. Just exit.
                }
            }
        };
#endif

        if (!LaunchArgs.HasGui)
        {
            // Run backend only (console mode)
            InitVRCVideoCacher().GetAwaiter().GetResult();
            return;
        }

        if (AdminCheck.IsRunningAsAdmin())
        {
            Logger.Warning("Application is running with administrator privileges. This is not recommended for security reasons.");
        }

        // Start backend on background thread
        Task.Run(async () =>
        {
            try
            {
                await InitVRCVideoCacher();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Backend error " + ex.Message + " " + ex.StackTrace);
            }
        });

        // Start the UI
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void InitializeLogger()
    {
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(new ExpressionTemplate(
                "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}" + Environment.NewLine + "{@x}",
                theme: TemplateTheme.Literate))
            .WriteTo.File(
                path: Path.Combine(LogsPath, "VRCVideoCacher.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 5)
            .WriteTo.Sentry(o =>
            {
                o.Dsn = Sentry_DSN;
                o.EnableLogs = true;
            });

        if (LaunchArgs.HasGui)
        {
            loggerConfiguration = loggerConfiguration.WriteTo.Sink(new UiLogSink());
        }

        Log.Logger = loggerConfiguration.CreateLogger();
        Logger = Log.ForContext("SourceContext", "Core");
    }

    public static async Task InitVRCVideoCacher()
    {
        try { Console.Title = $"VRCVideoCacher v{Version}{AdminCheck.GetAdminTitleWarning()}"; } catch { /* GUI mode, no console */ }

        Logger.Information("VRCVideoCacher version {Version} created by {Elly}, {Natsumi}, {Haxy}, {Hauskaz}, {DubyaDude}", Version, Creator_Elly, Creator_Natsumi, Creator_Haxy, Creator_Hauskaz, Creator_DubyaDude);

        if (AdminCheck.ShouldShowAdminWarning())
        {
            Logger.Error(AdminCheck.AdminWarningMessage);
        }

        Directory.CreateDirectory(UtilsPath);
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
        if (ConfigManager.Config.YtdlpAutoUpdate && !LaunchArgs.UseGlobalPath)
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

        await Task.Delay(-1);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    public static void DeleteCookieFile()
    {
        if (File.Exists(YtdlManager.CookiesPath))
        {
            File.Delete(YtdlManager.CookiesPath);
            Logger.Information("Deleted cookie file.");
        }
    }

    public static bool DoesCookieFileExist()
    {
        return File.Exists(YtdlManager.CookiesPath);
    }

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
            using var response = await client.GetAsync("https://www.youtube.com/new", cts.Token);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex)
        {
            Logger.Warning("Failed to validate cookies online: {Error}", ex.ToString());
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
