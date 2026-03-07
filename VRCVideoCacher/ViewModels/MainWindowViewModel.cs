using CodingSeb.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private string _statusText = Loc.Tr("ServerRunning");

    [ObservableProperty]
    private string _cacheStatusText = "Cache: 0 B";

    [ObservableProperty]
    private string _title = $"VRCVideoCacher v{Program.Version}";

    public DashboardViewModel Dashboard { get; }
    public SettingsViewModel Settings { get; }
    public CacheBrowserViewModel CacheBrowser { get; }
    public DownloadQueueViewModel DownloadQueue { get; }
    public LogViewerViewModel LogViewer { get; }
    public HistoryViewModel History { get; }

    public MainWindowViewModel()
    {
        Dashboard = new DashboardViewModel();
        Settings = new SettingsViewModel();
        CacheBrowser = new CacheBrowserViewModel();
        DownloadQueue = new DownloadQueueViewModel();
        LogViewer = new LogViewerViewModel();
        History = new HistoryViewModel();

        _currentView = Dashboard;

        _title += AdminCheck.GetAdminTitleWarning();

        // Subscribe to cache changes for status bar
        CacheManager.OnCacheChanged += (_, _) => UpdateCacheStatus();
        UpdateCacheStatus();

        // Refresh localized strings when language changes
        Loc.Instance.CurrentLanguageChanged += (_, _) => StatusText = Loc.Tr("ServerRunning");
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

    [RelayCommand]
    private void NavigateToDashboard() => CurrentView = Dashboard;

    [RelayCommand]
    private void NavigateToSettings() => CurrentView = Settings;

    [RelayCommand]
    private void NavigateToCacheBrowser() => CurrentView = CacheBrowser;

    [RelayCommand]
    private void NavigateToDownloadQueue() => CurrentView = DownloadQueue;

    [RelayCommand]
    private void NavigateToLogViewer() => CurrentView = LogViewer;

    [RelayCommand]
    private void NavigateToHistory() => CurrentView = History;
}
