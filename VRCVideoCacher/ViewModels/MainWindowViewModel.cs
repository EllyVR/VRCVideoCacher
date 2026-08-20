using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private string _statusText = Localizer.Get("ServerRunning");

    [ObservableProperty]
    private string _cacheStatusText = "Cache: 0 B";

    [ObservableProperty]
    private string _title = $"VRCVideoCacher v{Program.Version}";

    public DashboardViewModel Dashboard { get; }
    public SettingsViewModel Settings { get; }
    public RulesViewModel Rules { get; }
    public CacheBrowserViewModel CacheBrowser { get; }
    public DownloadQueueViewModel DownloadQueue { get; }
    public LogViewerViewModel LogViewer { get; }
    public HistoryViewModel History { get; }
    public AboutViewModel About { get; }

    public MainWindowViewModel()
    {
        Dashboard = new DashboardViewModel();
        Settings = new SettingsViewModel();
        Rules = new RulesViewModel();
        CacheBrowser = new CacheBrowserViewModel();
        DownloadQueue = new DownloadQueueViewModel();
        LogViewer = new LogViewerViewModel();
        History = new HistoryViewModel();
        About = new AboutViewModel();

        _currentView = Dashboard;

        // Subscribe to cache changes for status bar
        CacheManager.OnCacheChanged += (_, _) => UpdateCacheStatus();
        UpdateCacheStatus();

        // Refresh localized strings when language changes
        Localizer.LanguageChanged += (_, _) => StatusText = Localizer.Get("ServerRunning");
    }

    private void UpdateCacheStatus()
    {
        var size = CacheManager.GetTotalCacheSize();
        var maxSize = ConfigManager.Config.CacheMaxSizeInGb;

        if (maxSize > 0)
        {
            var maxBytes = (long)(maxSize * 1024 * 1024 * 1024);
            CacheStatusText = $"Cache: {FormatSize(size)} / {FormatSize(maxBytes)}";
        }
        else
        {
            CacheStatusText = $"Cache: {FormatSize(size)}";
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        if (bytes == 0) return "0 B";
        var mag = (int)Math.Log(bytes, 1024);
        var adjustedSize = bytes / Math.Pow(1024, mag);
        return $"{adjustedSize:N2} {suffixes[mag]}";
    }

    private async Task NavigateToAsync(ViewModelBase targetView)
    {
        if (CurrentView == targetView) return;

        if (CurrentView == Rules && Rules.HasChanges)
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var parentWindow = lifetime?.MainWindow;
            var canProceed = await Rules.CheckUnsavedChangesAsync(parentWindow);
            if (!canProceed) return;
        }

        CurrentView = targetView;
    }

    [RelayCommand]
    private async Task NavigateToDashboard() => await NavigateToAsync(Dashboard);

    [RelayCommand]
    private async Task NavigateToRules() => await NavigateToAsync(Rules);

    [RelayCommand]
    private async Task NavigateToSettings() => await NavigateToAsync(Settings);

    [RelayCommand]
    private async Task NavigateToCacheBrowser() => await NavigateToAsync(CacheBrowser);

    [RelayCommand]
    private async Task NavigateToDownloadQueue() => await NavigateToAsync(DownloadQueue);

    [RelayCommand]
    private async Task NavigateToLogViewer() => await NavigateToAsync(LogViewer);

    [RelayCommand]
    private async Task NavigateToHistory() => await NavigateToAsync(History);

    [RelayCommand]
    public async Task NavigateToAbout() => await NavigateToAsync(About);
}
