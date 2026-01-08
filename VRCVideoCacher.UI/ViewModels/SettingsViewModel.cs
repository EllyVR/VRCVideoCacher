using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VRCVideoCacher.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    // Server Settings
    [ObservableProperty]
    private string _webServerUrl = string.Empty;

    // Download Settings
    [ObservableProperty]
    private string _ytdlPath = string.Empty;

    [ObservableProperty]
    private bool _ytdlUseCookies;

    [ObservableProperty]
    private bool _ytdlAutoUpdate;

    [ObservableProperty]
    private string _ytdlAdditionalArgs = string.Empty;

    [ObservableProperty]
    private string _ytdlDubLanguage = string.Empty;

    [ObservableProperty]
    private int _ytdlDelay;

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

    // Patching
    [ObservableProperty]
    private bool _patchResonite;

    [ObservableProperty]
    private bool _patchVRC;

    // Updates
    [ObservableProperty]
    private bool _autoUpdate;

    // Blocked URLs
    public ObservableCollection<string> BlockedUrls { get; } = [];

    // Status
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasChanges;

    public SettingsViewModel()
    {
        LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        var config = ConfigManager.Config;

        WebServerUrl = config.ytdlWebServerURL;
        YtdlPath = config.ytdlPath;
        YtdlUseCookies = config.ytdlUseCookies;
        YtdlAutoUpdate = config.ytdlAutoUpdate;
        YtdlAdditionalArgs = config.ytdlAdditionalArgs;
        YtdlDubLanguage = config.ytdlDubLanguage;
        YtdlDelay = config.ytdlDelay;
        CachedAssetPath = config.CachedAssetPath;
        CacheYouTube = config.CacheYouTube;
        CacheYouTubeMaxResolution = config.CacheYouTubeMaxResolution;
        CacheYouTubeMaxLength = config.CacheYouTubeMaxLength;
        CacheMaxSizeInGb = config.CacheMaxSizeInGb;
        CachePyPyDance = config.CachePyPyDance;
        CacheVRDancing = config.CacheVRDancing;
        PatchResonite = config.PatchResonite;
        PatchVRC = config.PatchVRC;
        AutoUpdate = config.AutoUpdate;

        BlockedUrls.Clear();
        foreach (var url in config.BlockedUrls)
        {
            BlockedUrls.Add(url);
        }

        HasChanges = false;
        StatusMessage = string.Empty;
    }

    partial void OnWebServerUrlChanged(string value) => HasChanges = true;
    partial void OnYtdlPathChanged(string value) => HasChanges = true;
    partial void OnYtdlUseCookiesChanged(bool value) => HasChanges = true;
    partial void OnYtdlAutoUpdateChanged(bool value) => HasChanges = true;
    partial void OnYtdlAdditionalArgsChanged(string value) => HasChanges = true;
    partial void OnYtdlDubLanguageChanged(string value) => HasChanges = true;
    partial void OnYtdlDelayChanged(int value) => HasChanges = true;
    partial void OnCachedAssetPathChanged(string value) => HasChanges = true;
    partial void OnCacheYouTubeChanged(bool value) => HasChanges = true;
    partial void OnCacheYouTubeMaxResolutionChanged(int value) => HasChanges = true;
    partial void OnCacheYouTubeMaxLengthChanged(int value) => HasChanges = true;
    partial void OnCacheMaxSizeInGbChanged(float value) => HasChanges = true;
    partial void OnCachePyPyDanceChanged(bool value) => HasChanges = true;
    partial void OnCacheVRDancingChanged(bool value) => HasChanges = true;
    partial void OnPatchResoniteChanged(bool value) => HasChanges = true;
    partial void OnPatchVRCChanged(bool value) => HasChanges = true;
    partial void OnAutoUpdateChanged(bool value) => HasChanges = true;

    [RelayCommand]
    private void SaveSettings()
    {
        var config = ConfigManager.Config;

        config.ytdlWebServerURL = WebServerUrl;
        config.ytdlPath = YtdlPath;
        config.ytdlUseCookies = YtdlUseCookies;
        config.ytdlAutoUpdate = YtdlAutoUpdate;
        config.ytdlAdditionalArgs = YtdlAdditionalArgs;
        config.ytdlDubLanguage = YtdlDubLanguage;
        config.ytdlDelay = YtdlDelay;
        config.CachedAssetPath = CachedAssetPath;
        config.CacheYouTube = CacheYouTube;
        config.CacheYouTubeMaxResolution = CacheYouTubeMaxResolution;
        config.CacheYouTubeMaxLength = CacheYouTubeMaxLength;
        config.CacheMaxSizeInGb = CacheMaxSizeInGb;
        config.CachePyPyDance = CachePyPyDance;
        config.CacheVRDancing = CacheVRDancing;
        config.PatchResonite = PatchResonite;
        config.PatchVRC = PatchVRC;
        config.AutoUpdate = AutoUpdate;
        config.BlockedUrls = BlockedUrls.ToArray();

        ConfigManager.TrySaveConfig();
        HasChanges = false;
        StatusMessage = "Settings saved!";
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        LoadFromConfig();
        StatusMessage = "Settings reset to last saved values.";
    }

    [RelayCommand]
    private void AddBlockedUrl()
    {
        BlockedUrls.Add("https://");
        HasChanges = true;
    }

    [RelayCommand]
    private void RemoveBlockedUrl(string url)
    {
        BlockedUrls.Remove(url);
        HasChanges = true;
    }
}
