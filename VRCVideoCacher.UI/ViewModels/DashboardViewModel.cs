using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.UI.Views;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.UI.ViewModels;

public partial class MainWindowViewModel;

public partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _serverRunning = true;

    [ObservableProperty]
    private string _serverUrl = "http://localhost:9696";

    [ObservableProperty]
    private long _totalCacheSize;

    [ObservableProperty]
    private float _maxCacheSize;

    [ObservableProperty]
    private int _cachedVideoCount;

    [ObservableProperty]
    private int _downloadQueueCount;

    [ObservableProperty]
    private bool _cookiesValid;

    [ObservableProperty]
    private string _currentDownloadText = "None";

    public DashboardViewModel()
    {
        ServerUrl = ConfigManager.Config.ytdlWebServerURL;
        MaxCacheSize = ConfigManager.Config.CacheMaxSizeInGb;

        // Initial data load
        RefreshData();

        // Subscribe to events
        CacheManager.OnCacheChanged += OnCacheChanged;
        VideoDownloader.OnDownloadStarted += OnDownloadStarted;
        VideoDownloader.OnDownloadCompleted += OnDownloadCompleted;
        VideoDownloader.OnQueueChanged += OnQueueChanged;
        ConfigManager.OnConfigChanged += OnConfigChanged;
        VRCVideoCacher.Program.OnCookiesUpdated += OnCookiesUpdated;
    }

    private void OnCookiesUpdated()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CookiesValid = VRCVideoCacher.Program.IsCookiesEnabledAndValid();
        });
    }

    private void OnCacheChanged(string fileName, CacheChangeType changeType)
    {
        Dispatcher.UIThread.InvokeAsync(RefreshCacheStats);
    }

    private void OnDownloadStarted(VRCVideoCacher.Models.VideoInfo video)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentDownloadText = $"{video.UrlType}: {video.VideoId}";
        });
    }

    private void OnDownloadCompleted(VRCVideoCacher.Models.VideoInfo video, bool success)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentDownloadText = "None";
        });
    }

    private void OnQueueChanged()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            DownloadQueueCount = VideoDownloader.GetQueueCount();
        });
    }

    private void OnConfigChanged()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            ServerUrl = ConfigManager.Config.ytdlWebServerURL;
            MaxCacheSize = ConfigManager.Config.CacheMaxSizeInGb;
            CookiesValid = VRCVideoCacher.Program.IsCookiesEnabledAndValid();
        });
    }

    [RelayCommand]
    private void RefreshData()
    {
        RefreshCacheStats();
        DownloadQueueCount = VideoDownloader.GetQueueCount();
        CookiesValid = VRCVideoCacher.Program.IsCookiesEnabledAndValid();

        var currentDownload = VideoDownloader.GetCurrentDownload();
        CurrentDownloadText = currentDownload != null
            ? $"{currentDownload.UrlType}: {currentDownload.VideoId}"
            : "None";
    }

    private void RefreshCacheStats()
    {
        TotalCacheSize = CacheManager.GetTotalCacheSize();
        // Subtract 1 for index.html if it exists in the cache
        var count = CacheManager.GetCachedVideoCount();
        var assets = CacheManager.GetCachedAssets();
        if (assets.ContainsKey("index.html"))
            count--;
        CachedVideoCount = count;
    }

    [RelayCommand]
    private void OpenCacheFolder()
    {
        var cachePath = CacheManager.CachePath;
        if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start("explorer.exe", cachePath);
        }
        else if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Process.Start("xdg-open", cachePath);
        }
    }

    [RelayCommand]
    private async Task SetupCookieExtension()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new CookieSetupViewModel();
            var window = new CookieSetupWindow
            {
                DataContext = viewModel
            };

            viewModel.RequestClose += () => window.Close();

            await window.ShowDialog(desktop.MainWindow!);

            // Refresh cookies status after dialog closes
            CookiesValid = VRCVideoCacher.Program.IsCookiesEnabledAndValid();
        }
    }
}
