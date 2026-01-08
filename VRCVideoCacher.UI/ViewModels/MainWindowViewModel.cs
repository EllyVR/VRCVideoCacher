using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VRCVideoCacher.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private string _statusText = "Server running";

    [ObservableProperty]
    private string _cacheStatusText = "Cache: 0 B";

    public DashboardViewModel Dashboard { get; }
    public SettingsViewModel Settings { get; }
    public CacheBrowserViewModel CacheBrowser { get; }
    public DownloadQueueViewModel DownloadQueue { get; }
    public LogViewerViewModel LogViewer { get; }

    public MainWindowViewModel()
    {
        Dashboard = new DashboardViewModel();
        Settings = new SettingsViewModel();
        CacheBrowser = new CacheBrowserViewModel();
        DownloadQueue = new DownloadQueueViewModel();
        LogViewer = new LogViewerViewModel();

        _currentView = Dashboard;

        // Subscribe to cache changes for status bar
        CacheManager.OnCacheChanged += (_, _) => UpdateCacheStatus();
        UpdateCacheStatus();
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
}
