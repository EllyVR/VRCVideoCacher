using System.Reflection;
using System.Security.Cryptography;
using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;
using VRCVideoCacher.API;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher;

internal static class Program
{
    public static string YtdlpHash = string.Empty;
    public const string Version = "2025.7.16";
    public static readonly string CurrentProcessPath = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
    public static readonly ILogger Logger = Log.ForContext("SourceContext", "Core");
    
    public static async Task Main(string[] args)
    {
        Console.Title = $"VRCVideoCacher v{Version}";
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(new ExpressionTemplate(
                "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n{@x}",
                theme: TemplateTheme.Literate))
            .CreateLogger();
        const string elly = "Elly";
        const string natsumi = "Natsumi";
        const string haxy = "Haxy";
        Logger.Information("VRCVideoCacher version {Version} created by {Elly}, {Natsumi}, {Haxy}", Version, elly, natsumi, haxy);

        Directory.CreateDirectory(ConfigManager.Config.CachedAssetPath);
        await Updater.CheckForUpdates();
        Updater.Cleanup();
        if (Environment.CommandLine.Contains("--Reset"))
        {
            FileTools.Restore();
            Environment.Exit(0);
        }
        if (Environment.CommandLine.Contains("--Hash"))
        {
            Console.WriteLine(GetOurYtdlpHash());
            Environment.Exit(0);
        }
        Console.CancelKeyPress += (_, _) => ConsoleOnCancelKeyPress();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => OnAppQuit();
        
        YtdlpHash = GetOurYtdlpHash();
        await YtdlManager.TryDownloadYtdlp();
        YtdlManager.StartYtdlDownloadThread();
        AutoStartShortcut.TryUpdateShortcutPath();
        WebServer.Init();
        FileTools.BackupAndReplaceYtdl();
        await BulkPreCache.DownloadFileList();
        _ = YtdlManager.TryDownloadFfmpeg();

        if (ConfigManager.Config.ytdlUseCookies && !IsCookiesEnabledAndValid())
            Logger.Warning("No cookies found, please use the browser extension to send cookies or disable \"ytdlUseCookies\" in config.");

        CacheManager.Init();
        await WinGet.TryInstallPackages();
        if (YtdlManager.GlobalYtdlConfigExists())
            Logger.Error("Global YT-DLP config file found in \"%AppData%\\yt-dlp\" please delete it to avoid conflicts with VRCVideoCacher.");
        
        await Task.Delay(-1);
    }

    public static bool IsCookiesEnabledAndValid()
    {
        if (!ConfigManager.Config.ytdlUseCookies)
            return false;
        
        var cookiesPath = Path.Combine(CurrentProcessPath, "youtube_cookies.txt");
        if (!File.Exists(cookiesPath))
            return false;
        
        var cookies = File.ReadAllText(cookiesPath);
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

    public static Stream GetYtDlpStub()
    {
        return GetEmbeddedResource("VRCVideoCacher.yt-dlp-stub.exe");
    }
    
    private static Stream GetEmbeddedResource(string resourceName)
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

    private static void ConsoleOnCancelKeyPress()
    {
        OnAppQuit();
        Logger.Information("Press any key to continue...");
        Console.ReadKey();
        Environment.Exit(0);
    }

    private static void OnAppQuit()
    {
        FileTools.Restore();
        Logger.Information("Exiting...");
    }
}