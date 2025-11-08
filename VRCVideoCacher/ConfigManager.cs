using Newtonsoft.Json;
using Serilog;
using VRCVideoCacher.YTDL;

// ReSharper disable FieldCanBeMadeReadOnly.Global

namespace VRCVideoCacher;

public class ConfigManager
{
    public static readonly ConfigModel Config;
    private static readonly ILogger Log = Program.Logger.ForContext<ConfigManager>();
    private static readonly string ConfigFilePath;
    public static readonly string UtilsPath;

    static ConfigManager()
    {
        Log.Information("Loading config...");
        ConfigFilePath = Path.Combine(Program.DataPath, "Config.json");
        Log.Debug("Using config file path: {ConfigFilePath}", ConfigFilePath);

        if (!File.Exists(ConfigFilePath))
        {
            Config = new ConfigModel();
            FirstRun();
        }
        else
        {
            Config = JsonConvert.DeserializeObject<ConfigModel>(File.ReadAllText(ConfigFilePath)) ?? new ConfigModel();
        }
        if (Config.ytdlWebServerURL.EndsWith('/'))
            Config.ytdlWebServerURL = Config.ytdlWebServerURL.TrimEnd('/');

        UtilsPath = Path.GetDirectoryName(Config.ytdlPath) ?? string.Empty;
        if (!UtilsPath.EndsWith("Utils"))
            UtilsPath = Path.Combine(UtilsPath, "Utils");

        Directory.CreateDirectory(UtilsPath);
        
        Log.Information("Loaded config.");
        TrySaveConfig();
    }

    private static void TrySaveConfig()
    {
        var newConfig = JsonConvert.SerializeObject(Config, Formatting.Indented);
        var oldConfig = File.Exists(ConfigFilePath) ? File.ReadAllText(ConfigFilePath) : string.Empty;
        if (newConfig == oldConfig)
            return;
        
        Log.Information("Config changed, saving...");
        File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(Config, Formatting.Indented));
        Log.Information("Config saved.");
    }
    
    private static bool GetUserConfirmation(string prompt, bool defaultValue)
    {
        var defaultOption = defaultValue ? "Y/n" : "y/N";
        var message = $"{prompt} ({defaultOption}):";
        message = message.TrimStart();
        Log.Information(message);
        var input = Console.ReadLine();
        return string.IsNullOrEmpty(input) ? defaultValue : input.Equals("y", StringComparison.CurrentCultureIgnoreCase);
    }

    private static void FirstRun()
    {
        Log.Information("It appears this is your first time running VRCVideoCacher. Let's create a basic config file.");

        Config.CacheYouTube = GetUserConfirmation("Would you like to cache/download Youtube videos?", true);
        if (Config.CacheYouTube)
        {
            var maxResolution = GetUserConfirmation("Would you like to cache/download Youtube videos in 4k?", true);
            Config.CacheYouTubeMaxResolution = maxResolution ? 2160 : 1080;
        }

        var vrDancingPyPyChoice = GetUserConfirmation("Would you like to cache/download VRDancing & PyPyDance videos?", true);
        Config.CacheVRDancing = vrDancingPyPyChoice;
        Config.CachePyPyDance = vrDancingPyPyChoice;

        Log.Information("Would you like to use the companion extension to fetch youtube cookies? (This will fix bot errors, requires installation of the extension)");
        Log.Information("Extension can be found here: https://github.com/clienthax/VRCVideoCacherBrowserExtension");
        Config.ytdlUseCookies = GetUserConfirmation("", true);

        Config.PatchResonite = GetUserConfirmation("Would you like to patch Resonite Video Loading", false);
        
        if (OperatingSystem.IsWindows() && GetUserConfirmation("Would you like to add VRCVideoCacher to VRCX auto start?", true))
        {
            AutoStartShortcut.CreateShortcut();
        }

        if (YtdlManager.GlobalYtdlConfigExists() && GetUserConfirmation(@"Would you like to delete global YT-DLP config in %AppData%\yt-dlp\config?", true))
        {
            YtdlManager.DeleteGlobalYtdlConfig();
        }
    }
}

// ReSharper disable InconsistentNaming
public class ConfigModel
{
    public string ytdlWebServerURL = "http://localhost:9696";
    public string ytdlPath = OperatingSystem.IsWindows() ? "Utils\\yt-dlp.exe" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCVideoCacher/Utils/yt-dlp");
    public bool ytdlUseCookies = true;
    public bool ytdlAutoUpdate = true;
    public string ytdlAdditionalArgs = string.Empty;
    public string ytdlDubLanguage = string.Empty;
    public int ytdlDelay = 0;
    public string CachedAssetPath = "";
    public string[] BlockedUrls = ["https://na2.vrdancing.club/sampleurl.mp4"];
    public bool CacheYouTube = true;
    public int CacheYouTubeMaxResolution = 2160;
    public int CacheYouTubeMaxLength = 120;
    public float CacheMaxSizeInGb = 0;
    public bool CachePyPyDance = true;
    public bool CacheVRDancing = true;
    public bool PatchResonite = false;
    public bool PatchVRC = true;
    public bool AutoUpdate = true;
    public string[] PreCacheUrls = [];
    
}
// ReSharper restore InconsistentNaming