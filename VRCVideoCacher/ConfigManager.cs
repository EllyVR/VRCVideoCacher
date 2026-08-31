using System.Globalization;
using Jeek.Avalonia.Localization;
using Newtonsoft.Json;
using Serilog;
using VRCVideoCacher.Utils;

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
            if (!LaunchArgs.HasGui)
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
        Log.Information("{UserConfirmationMessage}", message);
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

        Log.Information("You'll need to install our companion extension to fetch youtube cookies (This will fix YouTube bot errors)");
        Log.Information("Chrome: https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge");
        Log.Information("Firefox: https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter/");
        Log.Information("More info: https://github.com/clienthax/VRCVideoCacherBrowserExtension");
        TrySaveConfig();
    }

    private static string GetSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Localizer.Languages.Contains(culture) ? culture : "en";
    }
}


public class ConfigModel
{
    // yt-dlp
    public string YtdlpWebServerUrl = "http://localhost:9696";
    public bool YtdlpUseCookies = true;
    public bool YtdlpAutoUpdate = true;
    public string YtdlpAdditionalArgs = string.Empty;
    public string YtdlpDubLanguage = string.Empty;

    // SABR restreaming: when an uncached YouTube video can't be direct-played, fetch it over SABR and
    // serve it to AVPro as a seekable HLS VOD instead of returning an unplayable URL.
    public bool SabrRestreamEnabled = true;
    // Max resolution for SABR STREAMING. Deliberately separate from CacheYouTubeMaxResolution, which
    // governs the cache download only.
    public int SabrMaxResolution = 1080;
    // Testing/eval: force ALL uncached YouTube videos through the SABR restream path (skip the
    // normal direct-URL resolution), so the feature can be exercised before SABR-only is widespread.
    public bool SabrRestreamForce = true;
    // Serve YouTube LIVE broadcasts over SABR as a sliding-window HLS stream instead of handing the
    // game yt-dlp's HLS manifest. Falls back to that manifest on any failure, so turning this off (or
    // hitting a bug) costs nothing but the old behaviour.
    public bool SabrLiveEnabled = true;
    // How many segments the live playlist advertises. This is a live edge, not a DVR: a longer window
    // costs disk and start-up latency and buys rewind that VRChat cannot use anyway (AVPro will not
    // scrub a playlist without EXT-X-ENDLIST, which a live playlist must never carry).
    public int SabrLiveWindowSegments = 6;
    // Base URL of the bgutil PO token provider. SABR uses the web client, which requires a GVS PO token for non premium accounts;
    // the token comes from this provider (auto-managed on our Deno at the default localhost port). A
    // non-loopback URL points at an externally-run provider and skips auto-management.
    //
    // Host is "localhost", NOT "127.0.0.1", deliberately: the bgutil server binds "::" (IPv6), which on
    // Windows is v6only, so a bare 127.0.0.1 connection is refused. "localhost" resolves to both ::1 and
    // 127.0.0.1 and the client falls through to whichever the server is actually on (IPv6 here, or IPv4
    // when the server had to fall back to 0.0.0.0 on IPv6-less machines).
    public string SabrPotBaseUrl = "http://localhost:4416";
    // Filter out YouTube's DRC (volume-normalised) audio tracks when picking SABR audio. yt-dlp tags
    // these with a "-drc" format-id suffix. Off by default (they play fine); a preference, not a fix.
    public bool SabrFilterDrcAudio = false;
    // Filter out YouTube's AI "super resolution" (upscaled) video tracks when picking SABR video. yt-dlp
    // tags these with a "-sr" format-id suffix and an "AI-upscaled" note. Off by default.
    public bool SabrFilterSuperResolution = false;
    // Filter out YouTube's AI "voice boosted" audio tracks when picking SABR audio. yt-dlp tags these
    // with a "-vb" format-id suffix and an "AI-voice boosted" note. Off by default.
    public bool SabrFilterVoiceBoostedAudio = false;
    // Testing/eval: force the SABR path to mux AAC audio instead of Opus, regardless of the Opus-in-MP4
    // decode check. Lets the AAC fallback path be exercised on a machine that CAN play Opus. Off by
    // default (the check picks the codec automatically).
    public bool SabrForceAacAudio = false;
    // Extra yt-dlp arguments appended to the SABR extraction run only (the -J link-extraction). Separate
    // from YtdlpAdditionalArgs, which applies to the non-SABR/fallback path — the SABR extraction is a
    // different command (web client, plugin dirs, PO token) so its overrides belong here.
    public string SabrAdditionalArgs = string.Empty;

    // Caching
    public string CachedAssetPath = "";
    public float CacheMaxSizeInGb = 10f;
    public bool CacheYouTube = false;
    public int CacheYouTubeMaxResolution = 1080;
    public int CacheYouTubeMaxLength = 120;
    public bool CachePyPyDance = false;
    public bool CacheVrDancing = false;
    public bool CacheGeneric = false;
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
    public bool StartMinimized = false;
    public bool StartWithSteamVr = true;
    public bool CookieSetupCompleted = false;
    public bool RedirectVRDancing = false;
    public bool ErrorPopups = true;

    // Localization
    public string Language = "en";
}
