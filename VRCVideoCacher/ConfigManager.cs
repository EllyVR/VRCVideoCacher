using System.Globalization;
using System.Text.RegularExpressions;
using Jeek.Avalonia.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using VRCVideoCacher.Models;
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
            {
                var jsonText = File.ReadAllText(ConfigFilePath);
                var jObj = JsonConvert.DeserializeObject<JObject>(jsonText);
                newConfig = JsonConvert.DeserializeObject<ConfigModel>(jsonText);

                if (newConfig != null && jObj != null)
                {
                    if (jObj["UriRules"] == null && (jObj["CacheYouTube"] != null || jObj["BlockedUrls"] != null || jObj["CachePyPyDance"] != null || jObj["RedirectVRDancing"] != null))
                    {
                        Log.Information("Migrating legacy config settings into UriRules...");
                        newConfig.UriRules = MigrateLegacyConfig(jObj);
                    }
                }
            }
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

        if (Config.UriRules == null || Config.UriRules.Count == 0)
        {
            Config.UriRules = ConfigModel.GetDefaultRules();
        }
        else
        {
            Config.UriRules = Config.UriRules.DistinctBy(r => r.Name + "|" + r.Pattern).ToList();
        }

        if (Config.YtdlpWebServerUrl.EndsWith('/'))
            Config.YtdlpWebServerUrl = Config.YtdlpWebServerUrl.TrimEnd('/');

        Log.Information("Loaded config.");
        TrySaveConfig();
    }



    private static List<UriRule> MigrateLegacyConfig(JObject json)
    {
        var rules = new List<UriRule>();

        // 1. VRDancing Redirect Rule (Specific override first)
        var redirectVrDancing = json.Value<bool?>("RedirectVRDancing") ?? false;
        rules.Add(new UriRule
        {
            Name = "VRDancing EU to NA Redirect",
            Pattern = @"^https?:\/\/eu2\.vrdancing\.club\/weekend\/(.*)$",
            Action = RuleAction.Redirect,
            RedirectTarget = "https://na2.vrdancing.club/weekend/$1",
            Enabled = redirectVrDancing
        });

        // 2. YouTube Music Redirect Rule (Specific override second)
        rules.Add(new UriRule
        {
            Name = "YouTube Music Redirect",
            Pattern = @"^https?:\/\/music\.youtube\.com\/(?:watch|playlist)?\?(?:.*?&)?v=([^&]+).*$",
            Action = RuleAction.Redirect,
            RedirectTarget = "https://youtube.com/watch?v=$1",
            Enabled = true
        });

        // 3. Blocked URLs / Block Action Rule (Specific override third)
        var blockedUrlsToken = json["BlockedUrls"] as JArray;
        if (blockedUrlsToken != null && blockedUrlsToken.Count > 0)
        {
            var blockedList = blockedUrlsToken.Select(t => t.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (blockedList.Count > 0)
            {
                var escapedPatterns = blockedList.Select(u =>
                {
                    if (u.StartsWith("^") || u.Contains(".*")) return u;
                    return Regex.Escape(u);
                });
                var combinedPattern = $"^(?:{string.Join("|", escapedPatterns)})";

                rules.Add(new UriRule
                {
                    Name = "Blocked URLs",
                    Pattern = combinedPattern,
                    Action = RuleAction.Block,
                    Enabled = true
                });
            }
        }
        else
        {
            rules.Add(new UriRule
            {
                Name = "Block Rickrolls",
                Pattern = @"^https?://(?:www\.)?youtube\.com/watch\?v=(?:dQw4w9WgXcQ|jzmz6K8K4L0|XfELJU1mRMg)",
                Action = RuleAction.Block,
                Enabled = true
            });
        }

        // 4. YouTube Domain Rule
        var cacheYouTube = json.Value<bool?>("CacheYouTube") ?? true;
        var maxRes = json.Value<int?>("CacheYouTubeMaxResolution") ?? 1080;
        var maxLength = json.Value<int?>("CacheYouTubeMaxLength") ?? 120;

        rules.Add(new UriRule
        {
            Name = "YouTube",
            Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*(?:youtube\.com|youtu\.be|youtube-nocookie\.com)(?:[\/?#]|$)",
            Action = RuleAction.Cache,
            MaxResolution = maxRes,
            MaxDurationMinutes = maxLength,
            Enabled = cacheYouTube
        });

        // 5. PyPyDance Domain Rule
        var cachePyPy = json.Value<bool?>("CachePyPyDance") ?? true;
        rules.Add(new UriRule
        {
            Name = "PyPyDance",
            Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*pypydance\.com(?:[\/?#]|$)",
            Action = RuleAction.Cache,
            Enabled = cachePyPy
        });

        // 6. VRDancing Domain Rule
        var cacheVrDancing = json.Value<bool?>("CacheVrDancing") ?? true;
        rules.Add(new UriRule
        {
            Name = "VRDancing",
            Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*vrdancing\.club(?:[\/?#]|$)",
            Action = RuleAction.Cache,
            Enabled = cacheVrDancing
        });

        // 7. Fallback Direct Rule
        rules.Add(new UriRule
        {
            Name = "Everything else",
            Pattern = @".*",
            Action = RuleAction.Direct,
            Enabled = true
        });

        return rules;
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
        Console.WriteLine($"VRCVideoCacher v{Program.Version} - First Run Setup");
        Console.WriteLine();
        Config.YtdlpUseCookies = GetUserConfirmation("Do you want to use YouTube cookies?", Config.YtdlpUseCookies);
        Config.YtdlpAutoUpdate = GetUserConfirmation("Do you want to auto-update utils (yt-dlp, FFmpeg, and Deno)?", Config.YtdlpAutoUpdate);
        Config.PatchVrChat = GetUserConfirmation("Do you want to patch VRChat?", Config.PatchVrChat);
        Config.PatchResonite = GetUserConfirmation("Do you want to patch Resonite?", Config.PatchResonite);
        Config.AutoUpdateVrcVideoCacher = GetUserConfirmation("Do you want to auto-update VRCVideoCacher?", Config.AutoUpdateVrcVideoCacher);
        TrySaveConfig();
    }

    private static string GetSystemLanguage()
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    }
}

public class ConfigModel
{
    public string YtdlpWebServerUrl = "http://localhost.youtube.com:9696";
    public bool YtdlpUseCookies = true;
    public bool YtdlpAutoUpdate = true;
    public string YtdlpAdditionalArgs = "";
    public string YtdlpDubLanguage = "";

    // Cache Settings
    public string CachedAssetPath = "";
    public float CacheMaxSizeInGb = 10.0f;
    public List<string> PreCacheUrls = [];

    // Rules Engine
    public List<UriRule> UriRules = GetDefaultRules();

    // Patching Settings
    public bool PatchResonite = false;
    public string ResonitePath = "";
    public bool PatchVrChat = true;

    // Video Cacher
    public bool AutoUpdateVrcVideoCacher = true;
    public bool VideoPlayersEnabled = true;
    public bool CloseToTray = true;
    public bool StartMinimized = false;
    public bool StartWithSteamVr = true;
    public bool CookieSetupCompleted = false;
    public bool ErrorPopups = true;

    // Localization
    public string Language = "en";

    public static List<UriRule> GetDefaultRules()
    {
        return
        [
            new UriRule
            {
                Name = "VRDancing EU to NA Redirect",
                Pattern = @"^https?:\/\/eu2\.vrdancing\.club\/weekend\/(.*)$",
                Action = RuleAction.Redirect,
                RedirectTarget = "https://na2.vrdancing.club/weekend/$1",
                Enabled = false
            },
            new UriRule
            {
                Name = "YouTube Music Redirect",
                Pattern = @"^https?:\/\/music\.youtube\.com\/(?:watch|playlist)?\?(?:.*?&)?v=([^&]+).*$",
                Action = RuleAction.Redirect,
                RedirectTarget = "https://youtube.com/watch?v=$1",
                Enabled = false
            },
            new UriRule
            {
                Name = "MightyGym CDN Direct",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*mightygymcdn\.nyc3\.cdn\.digitaloceanspaces\.com(?:[\/?#]|$)",
                Action = RuleAction.Direct,
                Enabled = true
            },
            new UriRule
            {
                Name = "Illumination Media Direct",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*(?:imvrcdn\.com|illumination\.media)(?:[\/?#]|$)",
                Action = RuleAction.Direct,
                Enabled = true
            },
            new UriRule
            {
                Name = "Virtual Film Institute Direct",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*virtualfilm\.institute(?:[\/?#]|$)",
                Action = RuleAction.Direct,
                Enabled = true
            },
            new UriRule
            {
                Name = "Block Rickrolls",
                Pattern = @"^https?://(?:www\.)?youtube\.com/watch\?v=(?:dQw4w9WgXcQ|jzmz6K8K4L0|XfELJU1mRMg)",
                Action = RuleAction.Block,
                Enabled = true
            },
            new UriRule
            {
                Name = "YouTube",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*(?:youtube\.com|youtu\.be|youtube-nocookie\.com)(?:[\/?#]|$)",
                Action = RuleAction.Cache,
                MaxResolution = 1080,
                MaxDurationMinutes = 120,
                Enabled = true
            },
            new UriRule
            {
                Name = "PyPyDance",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*pypydance\.com(?:[\/?#]|$)",
                Action = RuleAction.Cache,
                Enabled = true
            },
            new UriRule
            {
                Name = "VRDancing",
                Pattern = @"^https?:\/\/(?:[a-zA-Z0-9-]+\.)*vrdancing\.club(?:[\/?#]|$)",
                Action = RuleAction.Cache,
                Enabled = true
            },
            new UriRule
            {
                Name = "Everything else",
                Pattern = @".*",
                Action = RuleAction.Direct,
                Enabled = true
            }
        ];
    }
}
