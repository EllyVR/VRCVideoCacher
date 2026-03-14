using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CodingSeb.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.ViewModels;

public partial class HistoryItemViewModel : ViewModelBase
{
    public DateTime Timestamp { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? Id { get; init; }
    public UrlType Type { get; init; }
    public string? Author { get; init; }
    public bool HasAuthor => !string.IsNullOrEmpty(Author);

    private string? _title;
    private string? _thumbnailUrl;

    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrEmpty(_title)) return _title;
            return Url.Length > 60 ? Url[..57] + "..." : Url;
        }
    }

    public string TypeBadge => Type switch
    {
        UrlType.YouTube => "YouTube",
        UrlType.PyPyDance => "PyPyDance",
        UrlType.VRDancing => "VRDancing",
        _ => "Other"
    };

    public IBrush TypeBadgeColor => Type switch
    {
        UrlType.YouTube => new SolidColorBrush(Color.Parse("#CC0000")),
        UrlType.PyPyDance => new SolidColorBrush(Color.Parse("#4A90D9")),
        UrlType.VRDancing => new SolidColorBrush(Color.Parse("#7B68EE")),
        _ => new SolidColorBrush(Color.Parse("#555555"))
    };

    public string? ThumbnailUrl => _thumbnailUrl;

    public HistoryItemViewModel(History history, VideoInfoCache? meta)
    {
        Timestamp = history.Timestamp.ToLocalTime();
        Url = history.Url;
        Id = history.Id;
        Type = history.Type;
        _title = meta?.Title;
        Author = meta?.Author;
    }

    public async Task LoadMetadataAsync()
    {
        if(Id == null)
            return;

        // Load from DB
        var videoInfo = await YouTubeMetadataService.GetVideoMetadataAsync(Id);

        if (!string.IsNullOrEmpty(videoInfo?.Title))
        {
            _title = videoInfo.Title;
            OnPropertyChanged(nameof(DisplayTitle));
        }

        // Load thumbnail
        var thumbnailPath = ThumbnailManager.GetThumbnail(Id);
        if (Id.Length == 11 && string.IsNullOrEmpty(thumbnailPath))
            thumbnailPath = await YouTubeMetadataService.GetThumbnail(Id);

        if (!string.IsNullOrEmpty(thumbnailPath))
        {
            _thumbnailUrl = thumbnailPath;
            OnPropertyChanged(nameof(ThumbnailUrl));
        }
    }

    [RelayCommand]
    private void OpenUrl()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Url,
                UseShellExecute = true
            });
        }
        catch { /* Ignore errors */ }
    }

    [RelayCommand]
    private async Task CopyUrl()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(Url);
        }
    }
}

public partial class HistoryViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = string.Empty;

    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = [];

    public HistoryViewModel()
    {
        DatabaseManager.OnPlayHistoryAdded += () => Refresh();

        // Causes infinite loop because we update cache inside LoadMetadataAsync, which triggers this event again.
        // DatabaseManager.OnVideoInfoCacheUpdated += () => Refresh();

        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        var historyCache = DatabaseManager.GetVideoHistoryAsCache();

        HistoryItems.Clear();
        foreach (var item in historyCache.OrderByDescending(h => h.Timestamp))
        {
            HistoryItems.Add(item);
        }
        StatusText = string.Format(Loc.Tr("EntriesCountFormat"), HistoryItems.Count);

        _ = Task.Run(async () =>
        {
            foreach (var item in historyCache)
            {
                await item.LoadMetadataAsync();
            }
        });
    }
}
