using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.ViewModels;

public partial class CacheItemViewModel : ViewModelBase
{
    public string FileName { get; init; } = string.Empty;
    public string VideoId { get; init; } = string.Empty;
    public long Size { get; init; }
    public DateTime LastModified { get; init; }
    public string Extension { get; init; } = string.Empty;
    public UrlType Type { get; set; } = UrlType.Other;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _thumbnailSource = string.Empty;

    public string DisplayTitle => string.IsNullOrEmpty(Title) ? VideoId : Title;

    public string SizeFormatted => FormatSize(Size);

    // Event to notify parent when item is deleted
    public event Action<CacheItemViewModel>? OnDeleted;

    public async Task LoadMetadataAsync()
    {
        if (NicoVideoApiService.IsValidVideoId(VideoId))
            Type = UrlType.NicoVideo;
        else if (VideoId.Length == 11)
            Type = UrlType.YouTube;

        // Load from DB
        var videoInfo = Type switch
        {
            UrlType.YouTube => await YouTubeMetadataService.GetVideoMetadataAsync(VideoId),
            UrlType.NicoVideo => await NicoVideoApiService.GetVideoMetadataAsync(VideoId),
            _ => null
        };

        if (videoInfo != null)
        {
            Type = videoInfo.Type;
            if (!string.IsNullOrEmpty(videoInfo.Title))
            {
                Title = videoInfo.Title;
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }

        // Load thumbnail
        var thumbnailPath = ThumbnailManager.GetThumbnail(VideoId);
        if (string.IsNullOrEmpty(thumbnailPath))
            thumbnailPath = Type switch
            {
                UrlType.YouTube => await YouTubeMetadataService.GetThumbnail(VideoId),
                UrlType.NicoVideo => await NicoVideoApiService.GetThumbnail(VideoId),
                _ => null
            };

        if (!string.IsNullOrEmpty(thumbnailPath))
            ThumbnailSource = thumbnailPath;
    }

    private string GetWebUrl()
    {
        if (VideoId.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            VideoId.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return VideoId;

        return Type switch
        {
            UrlType.YouTube => $"https://www.youtube.com/watch?v={VideoId}",
            UrlType.NicoVideo => NicoVideoApiService.IsValidLiveId(VideoId)
                ? $"https://live.nicovideo.jp/watch/{VideoId}"
                : $"https://www.nicovideo.jp/watch/{VideoId}",
            _ => $"{ConfigManager.Config.YtdlpWebServerUrl}/{VideoId}.mp4"
        };
    }

    [RelayCommand]
    private void OpenUrl()
    {
        var url = GetWebUrl();
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { /* Ignore errors */ }
    }

    [RelayCommand]
    private async Task CopyUrl()
    {
        var url = $"{ConfigManager.Config.YtdlpWebServerUrl}/{FileName}";
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(url);
            }
        }
    }

    [RelayCommand]
    private void Delete()
    {
        CacheManager.DeleteCacheItem(FileName);
        OnDeleted?.Invoke(this);
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB"];
        if (bytes == 0) return "0 B";
        var mag = (int)Math.Log(bytes, 1024);
        mag = Math.Min(mag, suffixes.Length - 1);
        var adjustedSize = bytes / Math.Pow(1024, mag);
        return $"{adjustedSize:N2} {suffixes[mag]}";
    }
}

public partial class CacheBrowserViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private CacheItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public ObservableCollection<CacheItemViewModel> CachedVideos { get; } = [];
    public ObservableCollection<CacheItemViewModel> FilteredVideos { get; } = [];

    public CacheBrowserViewModel()
    {
        CacheManager.OnCacheChanged += OnCacheChanged;
    }

    private void OnCacheChanged(string fileName, CacheChangeType changeType)
    {
        Dispatcher.UIThread.InvokeAsync(RefreshCache);
    }

    partial void OnSearchFilterChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredVideos.Clear();

        var filter = SearchFilter?.ToLowerInvariant() ?? string.Empty;
        foreach (var video in CachedVideos)
        {
            if (string.IsNullOrEmpty(filter) ||
                video.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                video.VideoId.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredVideos.Add(video);
            }
        }

        StatusText = string.Format(Localizer.Get("VideosCountFormat"), FilteredVideos.Count, CachedVideos.Count);
    }

    [RelayCommand]
    private void RefreshCache()
    {
        CachedVideos.Clear();
        FilteredVideos.Clear();

        var cachedAssets = CacheManager.GetCachedAssets();
        var itemsToLoad = new List<CacheItemViewModel>();

        foreach (var (fileName, cache) in cachedAssets.OrderByDescending(x => x.Value.LastModified))
        {
            // Filter out non-video files like index.html
            if (fileName.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                continue;

            var videoId = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);

            var item = new CacheItemViewModel
            {
                FileName = fileName,
                VideoId = videoId,
                Size = cache.Size,
                LastModified = cache.LastModified,
                Extension = extension
            };

            // Subscribe to delete event
            item.OnDeleted += OnItemDeleted;

            CachedVideos.Add(item);
            itemsToLoad.Add(item);
        }

        ApplyFilter();

        // Load metadata (titles + thumbnails) asynchronously in the background
        _ = Task.Run(async () =>
        {
            foreach (var item in itemsToLoad)
            {
                await item.LoadMetadataAsync();
            }
        });
    }

    private void OnItemDeleted(CacheItemViewModel item)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CachedVideos.Remove(item);
            FilteredVideos.Remove(item);
            StatusText = string.Format(Localizer.Get("VideosCountFormat"), FilteredVideos.Count, CachedVideos.Count);
        });
    }

    [RelayCommand]
    private void DeleteAll()
    {
        CacheManager.ClearCache();
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        var cachePath = CacheManager.CachePath;
        if (OperatingSystem.IsWindows())
        {
            if (SelectedItem != null)
            {
                var filePath = Path.Join(cachePath, SelectedItem.FileName);
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            else
            {
                System.Diagnostics.Process.Start("explorer.exe", cachePath);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Process.Start("xdg-open", cachePath);
        }
    }

}
