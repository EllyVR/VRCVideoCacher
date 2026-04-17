using System.Collections.ObjectModel;
using System.Globalization;
using CodingSeb.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.API;

namespace VRCVideoCacher.ViewModels;

public record LanguageOption(string Code, string DisplayName);

public partial class SettingsViewModel : ViewModelBase
{
    // Server Settings
    [ObservableProperty]
    private string _webServerUrl = string.Empty;

    // Download Settings
    [ObservableProperty]
    private bool _ytdlUseCookies;

    [ObservableProperty]
    private bool _ytdlAutoUpdate;

    [ObservableProperty]
    private string _ytdlAdditionalArgs = string.Empty;

    [ObservableProperty]
    private string _ytdlDubLanguage = string.Empty;

    // Cache Settings
    [ObservableProperty]
    private string _cachedAssetPath = string.Empty;

    [ObservableProperty]
    private bool _cacheYouTube;

    [ObservableProperty]
    private int _cacheYouTubeMaxResolution;

    // Resolution options for the dropdown
    public int[] ResolutionOptions { get; } = [720, 1080, 1440, 2160];

    [ObservableProperty]
    private int _cacheYouTubeMaxLength;

    [ObservableProperty]
    private float _cacheMaxSizeInGb;

    [ObservableProperty]
    private bool _cachePyPyDance;

    [ObservableProperty]
    private bool _cacheVRDancing;

    [ObservableProperty]
    private bool _cacheOnly;

    // Patching
    [ObservableProperty]
    private bool _patchResonite;

    [ObservableProperty]
    private bool _patchVRC;

    [ObservableProperty] 
    private bool _redirectVRDancing;
    
    // Updates
    [ObservableProperty]
    private bool _autoUpdate;

    [ObservableProperty]
    private bool _closeToTray;

    [ObservableProperty]
    private bool _startMinimized;

    // Blocked URLs
    public ObservableCollection<string> BlockedUrls { get; } = [];

    [ObservableProperty]
    public string _blockRedirect = string.Empty;

    // Status
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _startWithSteamVr;

    [ObservableProperty]
    private bool _hasChanges;

    // Language selection
    public IReadOnlyList<LanguageOption> AvailableLanguageOptions =>
        Loc.Instance.AvailableLanguages
            .Select(code => new LanguageOption(code, GetLanguageDisplayName(code)))
            .ToList();

    [ObservableProperty]
    private LanguageOption? _selectedLanguageOption;

    partial void OnSelectedLanguageOptionChanged(LanguageOption? value)
    {
        if (value is null) return;
        Loc.Instance.CurrentLanguage = value.Code;
        ConfigManager.Config.Language = value.Code;
        ConfigManager.TrySaveConfig();
    }

    private static string GetLanguageDisplayName(string code)
    {
        try { return CultureInfo.GetCultureInfo(code).NativeName; }
        catch { return code; }
    }

    public SettingsViewModel()
    {
        ConfigManager.OnConfigChanged += LoadFromConfig;
        LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        var config = ConfigManager.Config;

        WebServerUrl = config.YtdlpWebServerUrl;
        YtdlUseCookies = config.YtdlpUseCookies;
        YtdlAutoUpdate = config.YtdlpAutoUpdate;
        YtdlAdditionalArgs = config.YtdlpAdditionalArgs;
        YtdlDubLanguage = config.YtdlpDubLanguage;
        CachedAssetPath = config.CachedAssetPath;
        CacheYouTube = config.CacheYouTube;
        CacheYouTubeMaxResolution = config.CacheYouTubeMaxResolution;
        CacheYouTubeMaxLength = config.CacheYouTubeMaxLength;
        CacheMaxSizeInGb = config.CacheMaxSizeInGb;
        CachePyPyDance = config.CachePyPyDance;
        CacheVRDancing = config.CacheVrDancing;
        CacheOnly = config.CacheOnly;
        PatchResonite = config.PatchResonite;
        PatchVRC = config.PatchVrChat;
        AutoUpdate = config.AutoUpdateVrcVideoCacher;
        CloseToTray = config.CloseToTray;
        StartMinimized = config.StartMinimized;
        StartWithSteamVr = config.StartWithSteamVr;
        RedirectVRDancing = config.RedirectVRDancing;
        BlockedUrls.Clear();
        foreach (var url in config.BlockedUrls)
        {
            BlockedUrls.Add(url);
        }
        BlockRedirect = config.BlockRedirect;

        SelectedLanguageOption = AvailableLanguageOptions.FirstOrDefault(o => o.Code == config.Language)
                                 ?? AvailableLanguageOptions.FirstOrDefault();

        HasChanges = false;
        StatusMessage = string.Empty;
    }

    private void SetHasChanges()
    {
        HasChanges = true;
        StatusMessage = Loc.Tr("SettingsUnsavedChanges");
    }

    partial void OnWebServerUrlChanged(string value) => SetHasChanges();
    partial void OnYtdlUseCookiesChanged(bool value) => SetHasChanges();
    partial void OnYtdlAutoUpdateChanged(bool value) => SetHasChanges();
    partial void OnYtdlAdditionalArgsChanged(string value) => SetHasChanges();
    partial void OnYtdlDubLanguageChanged(string value) => SetHasChanges();
    partial void OnCachedAssetPathChanged(string value) => SetHasChanges();
    partial void OnCacheYouTubeChanged(bool value) => SetHasChanges();
    partial void OnCacheYouTubeMaxResolutionChanged(int value) => SetHasChanges();
    partial void OnCacheYouTubeMaxLengthChanged(int value) => SetHasChanges();
    partial void OnCacheMaxSizeInGbChanged(float value) => SetHasChanges();
    partial void OnCachePyPyDanceChanged(bool value) => SetHasChanges();
    partial void OnCacheVRDancingChanged(bool value) => SetHasChanges();
    partial void OnCacheOnlyChanged(bool value) => SetHasChanges();
    partial void OnPatchResoniteChanged(bool value) => SetHasChanges();
    partial void OnPatchVRCChanged(bool value) => SetHasChanges();
    partial void OnAutoUpdateChanged(bool value) => SetHasChanges();
    partial void OnCloseToTrayChanged(bool value) => SetHasChanges();
    partial void OnStartMinimizedChanged(bool value) => SetHasChanges();
    partial void OnStartWithSteamVrChanged(bool value) => SetHasChanges();

    [RelayCommand]
    private void SaveSettings()
    {
        var config = ConfigManager.Config;

        if (config.YtdlpWebServerUrl != WebServerUrl)
        {
            config.YtdlpWebServerUrl = WebServerUrl;
            WebServer.Init();
        }

        config.YtdlpUseCookies = YtdlUseCookies;
        config.YtdlpAutoUpdate = YtdlAutoUpdate;
        config.YtdlpAdditionalArgs = YtdlAdditionalArgs;
        config.YtdlpDubLanguage = YtdlDubLanguage;
        config.CachedAssetPath = CachedAssetPath;
        config.CacheYouTube = CacheYouTube;
        config.CacheYouTubeMaxResolution = CacheYouTubeMaxResolution;
        config.CacheYouTubeMaxLength = CacheYouTubeMaxLength;
        config.CacheMaxSizeInGb = CacheMaxSizeInGb;
        config.CachePyPyDance = CachePyPyDance;
        config.CacheVrDancing = CacheVRDancing;
        config.CacheOnly = CacheOnly;
        config.PatchResonite = PatchResonite;
        config.PatchVrChat = PatchVRC;
        config.AutoUpdateVrcVideoCacher = AutoUpdate;
        config.CloseToTray = CloseToTray;
        config.StartMinimized = StartMinimized;
        config.StartWithSteamVr = StartMinimized;
        config.BlockedUrls = BlockedUrls.ToArray();
        config.BlockRedirect = BlockRedirect;
        config.RedirectVRDancing = RedirectVRDancing;
        ConfigManager.TrySaveConfig();
        HasChanges = false;
        StatusMessage = Loc.Tr("SettingsSaved");
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        LoadFromConfig();
        StatusMessage = Loc.Tr("SettingsReset");
    }

    [RelayCommand]
    private void AddBlockedUrl()
    {
        BlockedUrls.Add("https://");
        SetHasChanges();
    }

    [RelayCommand]
    private void RemoveBlockedUrl(string url)
    {
        BlockedUrls.Remove(url);
        SetHasChanges();
    }
}
