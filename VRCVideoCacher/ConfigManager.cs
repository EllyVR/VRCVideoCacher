using System.Globalization;
using CodingSeb.Localization;
using Newtonsoft.Json;
using Serilog;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

// ReSharper disable FieldCanBeMadeReadOnly.Global

namespace VRCVideoCacher;

public class ConfigManager
{
    public static ConfigModel Config { get; private set; }
    private static readonly ILogger Log = Program.Logger.ForContext<ConfigManager>();
    private static readonly string ConfigFilePath;

    // Events for UI
    public static event Action? OnConfigChanged;

    static ConfigManager()
    {
        Log.Information("Loading config...");
        ConfigFilePath = Path.Join(Program.DataPath, "Config.json");
        Log.Debug("Using config file path: {ConfigFilePath}", ConfigFilePath);

        ConfigModel? newConfig = null;
        try
        {
            if (File.Exists(ConfigFilePath))
                newConfig = JsonConvert.DeserializeObject<ConfigModel>(File.ReadAllText(ConfigFilePath));
            if (newConfig == null)
                newConfig = MigrateLegacyConfig();
            if (newConfig != null)
                Config = newConfig;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load config, creating new one...");
        }

        if (Config == null)
        {
            Log.Information("No valid config found, creating new one...");
            Config = new ConfigModel
            {
                Language = GetSystemLanguage()
            };
            if (!Program.HasGui)
                FirstRunConsole();
        }
        else
        {
            Log.Information("Config loaded successfully.");
        }
        
        if (Config.YtdlpWebServerUrl.EndsWith('/'))
            Config.YtdlpWebServerUrl = Config.YtdlpWebServerUrl.TrimEnd('/');

        Log.Information("Loaded config.");
        TrySaveConfig();
    }
    
    private static ConfigModel? MigrateLegacyConfig()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            // migrate legacy config if it exists
            var legacyConfigPath = Path.Join(Program.CurrentProcessPath, "config.json");
            if (!File.Exists(legacyConfigPath))
                return null;

            var legacyConfig = JsonConvert.DeserializeObject<LegacyConfigModel>(File.ReadAllText(legacyConfigPath));
            if (legacyConfig == null)
                return null;
            
            // move our files and folders
            var folderList = new List<string>
            {
                "Utils",
                "MetadataCache"
            };
            var fileList = new List<string>
            {
                "version.json",
                "yt-dlp.version.txt",
                "youtube_cookies.txt"
            };
            foreach (var folder in folderList)
            {
                var oldPath = Path.Join(Program.CurrentProcessPath, folder);
                var newPath = Path.Join(Program.DataPath, folder);
                if (Directory.Exists(oldPath))
                {
                    if (Directory.Exists(newPath))
                        Directory.Delete(newPath, true);
                    Directory.Move(oldPath, newPath);
                }
            }
            foreach (var file in fileList)
            {
                var oldPath = Path.Join(Program.CurrentProcessPath, file);
                var newPath = Path.Join(Program.DataPath, file);
                if (File.Exists(oldPath))
                {
                    if (File.Exists(newPath))
                        File.Delete(newPath);
                    File.Move(oldPath, newPath);
                }
            }

            // Use existing cache directory if it has cached assets
            var oldCachePath = Path.Join(Program.CurrentProcessPath, "CachedAssets");
            var isCacheDirInUse = Directory.Exists(oldCachePath) && Directory.GetFiles(oldCachePath).Length > 1;
            var newCachePath = isCacheDirInUse ? oldCachePath : legacyConfig.CachedAssetPath;

            File.Delete(legacyConfigPath);
            Log.Information("Migrated legacy config from {LegacyConfigPath}", legacyConfigPath);
            return new ConfigModel
            {
                YtdlpWebServerUrl = legacyConfig.ytdlWebServerURL,
                YtdlpGlobalPath = string.IsNullOrEmpty(legacyConfig.ytdlPath),
                YtdlpUseCookies = legacyConfig.ytdlUseCookies,
                YtdlpAutoUpdate = legacyConfig.ytdlAutoUpdate,
                YtdlpAdditionalArgs = legacyConfig.ytdlAdditionalArgs,
                YtdlpDubLanguage = legacyConfig.ytdlDubLanguage,
                CachedAssetPath = newCachePath,
                BlockedUrls = legacyConfig.BlockedUrls,
                BlockRedirect = legacyConfig.BlockRedirect,
                CacheYouTube = legacyConfig.CacheYouTube,
                CacheYouTubeMaxResolution = legacyConfig.CacheYouTubeMaxResolution,
                CacheYouTubeMaxLength = legacyConfig.CacheYouTubeMaxLength,
                CacheMaxSizeInGb = legacyConfig.CacheMaxSizeInGb,
                CachePyPyDance = legacyConfig.CachePyPyDance,
                CacheVrDancing = legacyConfig.CacheVRDancing,
                PatchResonite = legacyConfig.PatchResonite,
                ResonitePath = legacyConfig.ResonitePath,
                PatchVrChat = legacyConfig.PatchVRC,
                AutoUpdateVrcVideoCacher = legacyConfig.AutoUpdate
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to migrate legacy config, it may be corrupted. Recreating...");
            return null;
        }
    }

    public static void TrySaveConfig()
    {
        var newConfig = JsonConvert.SerializeObject(Config, Formatting.Indented);
        var oldConfig = File.Exists(ConfigFilePath) ? File.ReadAllText(ConfigFilePath) : string.Empty;
        if (newConfig == oldConfig)
            return;

        Log.Information("Config changed, saving...");
        File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(Config, Formatting.Indented));
        Log.Information("Config saved.");
        OnConfigChanged?.Invoke();
        CacheManager.TryFlushCache();
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

    private static void FirstRunConsole()
    {
        Log.Information("It appears this is your first time running VRCVideoCacher. Let's create a basic config file.");

        var autoSetup = GetUserConfirmation("Would you like to use VRCVideoCacher for only fixing YouTube videos?", true);
        if (autoSetup)
        {
            Log.Information("Basic config created. You can modify it later in the Config.json file.");
        }
        else
        {
            Config.CacheYouTube = GetUserConfirmation("Would you like to cache/download Youtube videos?", true);
            if (Config.CacheYouTube)
            {
                var maxResolution = GetUserConfirmation("Would you like to cache/download Youtube videos in 4k?", true);
                Config.CacheYouTubeMaxResolution = maxResolution ? 2160 : 1080;
            }

            var vrDancingPyPyChoice = GetUserConfirmation("Would you like to cache/download VRDancing & PyPyDance videos?", true);
            Config.CacheVrDancing = vrDancingPyPyChoice;
            Config.CachePyPyDance = vrDancingPyPyChoice;

            Config.PatchResonite = GetUserConfirmation("Would you like to enable Resonite support?", false);
        }

        if (OperatingSystem.IsWindows() && GetUserConfirmation("Would you like to add VRCVideoCacher to VRCX auto start?", true))
        {
            AutoStartShortcut.CreateShortcut();
        }

        if (YtdlpGlobalConfig.GlobalYtdlConfigExists() && GetUserConfirmation(@"Would you like to delete global YT-DLP config in %AppData%\yt-dlp\config? (this is necessary for VRCVideoCacher to function)", true))
        {
            YtdlpGlobalConfig.DeleteGlobalYtdlConfig();
        }

        Log.Information("You'll need to install our companion extension to fetch youtube cookies (This will fix YouTube bot errors)");
        Log.Information("Chrome: https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge");
        Log.Information("Firefox: https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter/");
        Log.Information("More info: https://github.com/clienthax/VRCVideoCacherBrowserExtension");
        TrySaveConfig();
    }
    
    private static string GetSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Loc.Instance.AvailableLanguages.Contains(culture) ? culture : "en";
    }
}


public class ConfigModel
{
    // yt-dlp
    public string YtdlpWebServerUrl = "http://localhost:9696";
    public bool YtdlpGlobalPath = false;
    public bool YtdlpUseCookies = true;
    public bool YtdlpAutoUpdate = true;
    public string YtdlpAdditionalArgs = string.Empty;
    public string YtdlpDubLanguage = string.Empty;

    // Caching
    public string CachedAssetPath = "";
    public float CacheMaxSizeInGb = 10f;
    public bool CacheYouTube = false;
    public int CacheYouTubeMaxResolution = 1080;
    public int CacheYouTubeMaxLength = 120;
    public bool CachePyPyDance = false;
    public bool CacheVrDancing = false;
    public bool CacheOnly = false;

    // Cache Rules
    public string[] BlockedUrls = ["https://na2.vrdancing.club/sampleurl.mp4"];
    public string BlockRedirect = "https://www.youtube.com/watch?v=byv2bKekeWQ";
    public string[] PreCacheUrls = [];

    // Patching
    public bool PatchResonite = false;
    public string ResonitePath = "";
    public bool PatchVrChat = true;

    // Video Cacher
    public bool AutoUpdateVrcVideoCacher = true;
    public bool CloseToTray = true;
    public bool CookieSetupCompleted = false;

    // Localization
    public string Language = "en";
}



// ReSharper disable InconsistentNaming
public class LegacyConfigModel
{
    public string ytdlWebServerURL = "http://localhost:9696";
    public string ytdlPath = OperatingSystem.IsWindows() ? "Utils\\yt-dlp.exe" : Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCVideoCacher/Utils/yt-dlp");
    public bool ytdlUseCookies = true;
    public bool ytdlAutoUpdate = true;
    public string ytdlAdditionalArgs = string.Empty;
    public string ytdlDubLanguage = string.Empty;
    public int ytdlDelay = 0;
    public string CachedAssetPath = "";
    public string[] BlockedUrls = ["https://na2.vrdancing.club/sampleurl.mp4"];
    public string BlockRedirect = "https://www.youtube.com/watch?v=byv2bKekeWQ";
    public bool CacheYouTube = false;
    public int CacheYouTubeMaxResolution = 1080;
    public int CacheYouTubeMaxLength = 120;
    public float CacheMaxSizeInGb = 0;
    public bool CachePyPyDance = false;
    public bool CacheVRDancing = false;
    public bool PatchResonite = false;
    public string ResonitePath = "";
    public bool PatchVRC = true;
    public bool AutoUpdate = true;
    public string[] PreCacheUrls = [];
    
}
// ReSharper restore InconsistentNaming