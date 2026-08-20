using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.API;

namespace VRCVideoCacher.ViewModels;

public record LanguageOption(string Code, string DisplayName);

public partial class SettingsViewModel : ViewModelBase
{
    private bool _isLoadingConfig;

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
    private float _cacheMaxSizeInGb;

    // Patching
    [ObservableProperty]
    private bool _patchResonite;

    [ObservableProperty]
    private bool _patchVRC;

    // Updates
    [ObservableProperty]
    private bool _autoUpdate;

    [ObservableProperty]
    private bool _closeToTray;

    [ObservableProperty]
    private bool _startMinimized;

    // Status
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessageColor = string.Empty;

    [ObservableProperty]
    private bool _startWithSteamVr;

    [ObservableProperty]
    private bool _hasChanges;

    [ObservableProperty]
    private bool _errorPopups;

    // Language selection
    public IReadOnlyList<LanguageOption> AvailableLanguageOptions =>
        Localizer.Languages
            .Select(code => new LanguageOption(code, GetLanguageDisplayName(code)))
            .ToList();

    [ObservableProperty]
    private LanguageOption? _selectedLanguageOption;

    partial void OnSelectedLanguageOptionChanged(LanguageOption? value)
    {
        if (value is null) return;
        Localizer.Language = value.Code;
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
        _isLoadingConfig = true;
        var config = ConfigManager.Config;

        WebServerUrl = config.YtdlpWebServerUrl;
        YtdlUseCookies = config.YtdlpUseCookies;
        YtdlAutoUpdate = config.YtdlpAutoUpdate;
        YtdlAdditionalArgs = config.YtdlpAdditionalArgs;
        YtdlDubLanguage = config.YtdlpDubLanguage;
        CachedAssetPath = config.CachedAssetPath;
        CacheMaxSizeInGb = config.CacheMaxSizeInGb;
        PatchResonite = config.PatchResonite;
        PatchVRC = config.PatchVrChat;
        AutoUpdate = config.AutoUpdateVrcVideoCacher;
        CloseToTray = config.CloseToTray;
        StartMinimized = config.StartMinimized;
        StartWithSteamVr = config.StartWithSteamVr;
        ErrorPopups = config.ErrorPopups;

        SelectedLanguageOption = AvailableLanguageOptions.FirstOrDefault(o => o.Code == config.Language)
                                 ?? AvailableLanguageOptions.FirstOrDefault();

        HasChanges = false;
        StatusMessage = string.Empty;
        StatusMessageColor = "#81C784";
        _isLoadingConfig = false;
    }

    private void SetHasChanges()
    {
        if (_isLoadingConfig)
        {
            return;
        }

        HasChanges = true;
        StatusMessage = Localizer.Get("SettingsUnsavedChanges");
        StatusMessageColor = "#FFB74D";
    }

    partial void OnWebServerUrlChanged(string value) => SetHasChanges();
    partial void OnYtdlUseCookiesChanged(bool value) => SetHasChanges();
    partial void OnYtdlAutoUpdateChanged(bool value) => SetHasChanges();
    partial void OnYtdlAdditionalArgsChanged(string value) => SetHasChanges();
    partial void OnYtdlDubLanguageChanged(string value) => SetHasChanges();
    partial void OnCachedAssetPathChanged(string value) => SetHasChanges();
    partial void OnCacheMaxSizeInGbChanged(float value) => SetHasChanges();
    partial void OnPatchResoniteChanged(bool value) => SetHasChanges();
    partial void OnPatchVRCChanged(bool value) => SetHasChanges();
    partial void OnAutoUpdateChanged(bool value) => SetHasChanges();
    partial void OnCloseToTrayChanged(bool value) => SetHasChanges();
    partial void OnStartMinimizedChanged(bool value) => SetHasChanges();
    partial void OnStartWithSteamVrChanged(bool value) => SetHasChanges();
    partial void OnErrorPopupsChanged(bool value) => SetHasChanges();

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
        config.CacheMaxSizeInGb = CacheMaxSizeInGb;
        config.PatchResonite = PatchResonite;
        config.PatchVrChat = PatchVRC;
        config.AutoUpdateVrcVideoCacher = AutoUpdate;
        config.CloseToTray = CloseToTray;
        config.StartMinimized = StartMinimized;
        config.StartWithSteamVr = StartWithSteamVr;
        config.ErrorPopups = ErrorPopups;
        ConfigManager.TrySaveConfig();
        HasChanges = false;
        StatusMessage = Localizer.Get("SettingsSaved");
        StatusMessageColor = "#81C784";
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        LoadFromConfig();
        StatusMessage = Localizer.Get("SettingsReset");
        StatusMessageColor = "#81C784";
    }
}
