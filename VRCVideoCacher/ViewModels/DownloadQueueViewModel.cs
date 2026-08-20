using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Models;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.ViewModels;

public partial class DownloadItemViewModel : ViewModelBase
{
    public string VideoUrl { get; init; } = string.Empty;
    public string VideoId { get; init; } = string.Empty;
    public string UrlType { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
}

public partial class DownloadQueueViewModel : ViewModelBase
{
    [ObservableProperty]
    private DownloadItemViewModel? _currentDownload;

    [ObservableProperty]
    private string _currentStatus = "Idle";

    [ObservableProperty]
    private string _manualUrl = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<DownloadItemViewModel> QueuedDownloads { get; } = [];

    public DownloadQueueViewModel()
    {
        RefreshQueue();

        VideoDownloader.OnDownloadStarted += OnDownloadStarted;
        VideoDownloader.OnDownloadCompleted += OnDownloadCompleted;
        VideoDownloader.OnQueueChanged += OnQueueChanged;
    }

    private void OnDownloadStarted(VideoInfo video)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentDownload = new DownloadItemViewModel
            {
                VideoUrl = video.VideoUrl,
                VideoId = video.VideoId,
                UrlType = video.UrlType.ToString(),
                Format = video.DownloadFormat.ToString()
            };
            CurrentStatus = $"Downloading {video.VideoId}...";
            RefreshQueue();
        });
    }

    private void OnDownloadCompleted(VideoInfo video, bool success)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentDownload = null;
            CurrentStatus = success ? "Completed" : "Failed";
            StatusMessage = success
                ? $"Downloaded: {video.VideoId}"
                : $"Failed to download: {video.VideoId}";
            RefreshQueue();
        });
    }

    private void OnQueueChanged()
    {
        Dispatcher.UIThread.InvokeAsync(RefreshQueue);
    }

    [RelayCommand]
    private void RefreshQueue()
    {
        QueuedDownloads.Clear();

        var queue = VideoDownloader.GetQueueSnapshot();
        foreach (var video in queue)
        {
            QueuedDownloads.Add(new DownloadItemViewModel
            {
                VideoUrl = video.VideoUrl,
                VideoId = video.VideoId,
                UrlType = video.UrlType.ToString(),
                Format = video.DownloadFormat.ToString()
            });
        }

        var current = VideoDownloader.GetCurrentDownload();
        if (current != null)
        {
            CurrentDownload = new DownloadItemViewModel
            {
                VideoUrl = current.VideoUrl,
                VideoId = current.VideoId,
                UrlType = current.UrlType.ToString(),
                Format = current.DownloadFormat.ToString()
            };
            CurrentStatus = $"Downloading {current.VideoId}...";
        }
        else
        {
            CurrentDownload = null;
            if (QueuedDownloads.Count == 0)
            {
                CurrentStatus = "Idle";
            }
        }
    }

    [RelayCommand]
    private async Task AddManualDownload()
    {
        if (string.IsNullOrWhiteSpace(ManualUrl))
        {
            StatusMessage = "Please enter a URL";
            return;
        }

        var inputUrl = ManualUrl.Trim();
        ManualUrl = string.Empty;

        try
        {
            // Check if input URL is a playlist link (e.g. contains "list=" or "/playlist")
            if (inputUrl.Contains("list=", StringComparison.OrdinalIgnoreCase) || inputUrl.Contains("/playlist", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Fetching playlist items...";
                var playlistUrls = await VideoId.ExtractPlaylistUrls(inputUrl);

                if (playlistUrls.Count > 0)
                {
                    int addedCount = 0;
                    foreach (var videoUrl in playlistUrls)
                    {
                        var info = await VideoId.GetVideoId(videoUrl, true);
                        if (info != null)
                        {
                            VideoDownloader.QueueDownload(info);
                            addedCount++;
                        }
                    }
                    StatusMessage = $"Added {addedCount} playlist video(s) to queue";
                    return;
                }
            }

            var videoInfo = await VideoId.GetVideoId(inputUrl, true);
            if (videoInfo != null)
            {
                VideoDownloader.QueueDownload(videoInfo);
                StatusMessage = $"Added to queue: {videoInfo.VideoId}";
            }
            else
            {
                StatusMessage = "Could not parse URL";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearQueue()
    {
        VideoDownloader.ClearQueue();
        StatusMessage = "Download queue cleared";
    }
}
