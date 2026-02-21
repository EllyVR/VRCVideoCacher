using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Elevator;
using VRCVideoCacher.Utils;
using VRCVideoCacher.Views;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.ViewModels;

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
    private string _cookieStatus = "Not Set";

    [ObservableProperty]
    private string _currentDownloadText = "None";

    [ObservableProperty]
    private bool _hostState;

    public DashboardViewModel()
    {
        ServerUrl = ConfigManager.Config.YtdlpWebServerUrl;
        MaxCacheSize = ConfigManager.Config.CacheMaxSizeInGb;
        HostState = ElevatorManager.HasHostsLine;

        // Initial data load
        RefreshData();

        // Subscribe to events
        CacheManager.OnCacheChanged += OnCacheChanged;
        VideoDownloader.OnDownloadStarted += OnDownloadStarted;
        VideoDownloader.OnDownloadCompleted += OnDownloadCompleted;
        VideoDownloader.OnQueueChanged += OnQueueChanged;
        ConfigManager.OnConfigChanged += OnConfigChanged;
        Program.OnCookiesUpdated += OnCookiesUpdated;
    }

    private void OnCookiesUpdated()
    {
        _ = ValidateCookiesAsync();
    }

    private void OnCacheChanged(string fileName, CacheChangeType changeType)
    {
        Dispatcher.UIThread.InvokeAsync(RefreshCacheStats);
    }

    private void OnDownloadStarted(Models.VideoInfo video)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentDownloadText = $"{video.UrlType}: {video.VideoId}";
        });
    }

    private void OnDownloadCompleted(Models.VideoInfo video, bool success)
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
            ServerUrl = ConfigManager.Config.YtdlpWebServerUrl;
            MaxCacheSize = ConfigManager.Config.CacheMaxSizeInGb;
        });
        _ = ValidateCookiesAsync();
    }

    [RelayCommand]
    private void RefreshData()
    {
        RefreshCacheStats();
        DownloadQueueCount = VideoDownloader.GetQueueCount();

        var currentDownload = VideoDownloader.GetCurrentDownload();
        CurrentDownloadText = currentDownload != null
            ? $"{currentDownload.UrlType}: {currentDownload.VideoId}"
            : "None";

        _ = ValidateCookiesAsync();
    }

    [RelayCommand]
    private void ToggleHost()
    {
        ElevatorManager.ToggleHostLine();
        Dispatcher.UIThread.Post(() => { HostState = ElevatorManager.HasHostsLine; });
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

    private async Task ValidateCookiesAsync()
    {
        if (!Program.IsCookiesEnabledAndValid())
        {
            Dispatcher.UIThread.Post(() => CookieStatus = "Not Set");
            return;
        }

        Dispatcher.UIThread.Post(() => CookieStatus = "Checking...");

        var result = await Program.ValidateCookiesAsync();
        Dispatcher.UIThread.Post(() =>
        {
            CookieStatus = result switch
            {
                true => "Valid",
                false => "Expired",
                null => "Unknown"
            };
        });
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
            _ = ValidateCookiesAsync();
        }
    }
}
