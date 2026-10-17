using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Views;

namespace VRCVideoCacher.ViewModels;

public partial class HistoryItemViewModel : ViewModelBase
{
    public int Key { get; init; }
    public DateTime Timestamp { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? Id { get; init; }
    public UrlType Type { get; init; }
    public string? Author { get; set; }
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
        UrlType.NicoVideo => "NicoNico",
        _ => "Other"
    };

    public IBrush TypeBadgeColor => Type switch
    {
        UrlType.YouTube => new SolidColorBrush(Color.Parse("#CC0000")),
        UrlType.PyPyDance => new SolidColorBrush(Color.Parse("#4A90D9")),
        UrlType.VRDancing => new SolidColorBrush(Color.Parse("#7B68EE")),
        UrlType.NicoVideo => new SolidColorBrush(Color.Parse("#1B1B1B")),
        _ => new SolidColorBrush(Color.Parse("#555555"))
    };

    public string? ThumbnailUrl => _thumbnailUrl;

    public HistoryItemViewModel(History history, VideoInfoCache? meta)
    {
        Key = history.Key;
        Timestamp = history.Timestamp.ToLocalTime();
        Url = history.Url;
        Id = history.Id;
        Type = history.Type;
        _title = meta?.Title;
        Author = meta?.Author;
    }

    public void SetMetadata(string? title, string? thumbnailUrl)
    {
        if (!string.IsNullOrEmpty(title))
        {
            _title = title;
            OnPropertyChanged(nameof(DisplayTitle));
        }
        if (!string.IsNullOrEmpty(thumbnailUrl))
        {
            _thumbnailUrl = thumbnailUrl;
            OnPropertyChanged(nameof(ThumbnailUrl));
        }
    }

    public async Task<(string? DisplayTitle, string? ThumbnailUrl)> LoadMetadataAsync()
    {
        if (Id != null)
        {
            // Load from DB
            var videoInfo = Type switch
            {
                UrlType.YouTube => await YouTubeMetadataService.GetVideoMetadataAsync(Id),
                UrlType.NicoVideo => await NicoVideoApiService.GetVideoMetadataAsync(Id),
                _ => null
            };

            if (!string.IsNullOrEmpty(videoInfo?.Title))
            {
                _title = videoInfo.Title;
                OnPropertyChanged(nameof(DisplayTitle));
            }

            if (!string.IsNullOrEmpty(videoInfo?.Author))
            {
                Author = videoInfo.Author;
                OnPropertyChanged(nameof(Author));
            }

            // Load thumbnail
            var thumbnailPath = ThumbnailManager.GetThumbnail(Id);
            if (string.IsNullOrEmpty(thumbnailPath))
                thumbnailPath = Type switch
                {
                    UrlType.YouTube => await YouTubeMetadataService.GetThumbnail(Id),
                    UrlType.NicoVideo => await NicoVideoApiService.GetThumbnail(Id),
                    _ => null
                };

            if (!string.IsNullOrEmpty(thumbnailPath))
            {
                _thumbnailUrl = thumbnailPath;
                OnPropertyChanged(nameof(ThumbnailUrl));
            }
        }

        return (DisplayTitle, ThumbnailUrl);
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

    [RelayCommand]
    private void Delete()
    {
        // Removes the video from history entirely (all of its play records), so it doesn't just reappear
        // from an earlier play. DatabaseManager raises OnPlayHistoryChanged, which the list refreshes on.
        DatabaseManager.DeletePlayHistoryForVideo(Id, Url);
    }
}

public partial class HistoryViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int _maxSize = ConfigManager.Config.HistoryMaxSize;

    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = [];

    public HistoryViewModel()
    {
        DatabaseManager.OnPlayHistoryAdded += () => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        DatabaseManager.OnPlayHistoryChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

        // NB: deliberately NOT subscribing to OnVideoInfoCacheUpdated. The background metadata load writes
        // to VideoInfoCache, which raises that event — refreshing on it looped endlessly, and the old fix
        // (a guard that dropped refreshes while metadata loaded) also swallowed delete-triggered refreshes.
        // Metadata is applied to the rows in place by LoadMetadata, so a full reload here isn't needed.

        Refresh();
    }

    partial void OnMaxSizeChanged(int value)
    {
        if (value <= 0)
            return;

        ConfigManager.Config.HistoryMaxSize = value;
        ConfigManager.TrySaveConfig();
        // Apply the new cap immediately (trims the DB if it was lowered), then reload the list.
        DatabaseManager.TrimPlayHistory(value);
        Refresh();
    }

    private bool _isLoadingMetadata;
    private readonly List<HistoryItemViewModel> _pendingMetadata = [];

    [RelayCommand]
    private void Refresh()
    {
        var fresh = DatabaseManager
            .GetVideoHistoryAsCache(limit: ConfigManager.Config.HistoryMaxSize, distinctOnly: true)
            .OrderByDescending(h => h.Timestamp)
            .ToList();

        // Reuse the existing row VMs (keyed by the play record's PK) so their already-loaded thumbnails and
        // commands survive. Only rows genuinely new to the list are freshly created — so a delete just drops
        // one row, leaving every other image and delete button untouched (no flicker, no stale click target).
        var existing = HistoryItems.ToDictionary(i => i.Key);
        var desired = new List<HistoryItemViewModel>(fresh.Count);
        var newItems = new List<HistoryItemViewModel>();
        foreach (var item in fresh)
        {
            if (existing.TryGetValue(item.Key, out var reused))
            {
                desired.Add(reused);
            }
            else
            {
                desired.Add(item);
                newItems.Add(item);
            }
        }

        // Apply as an in-place diff rather than Clear()+re-add, so unchanged containers are never torn down.
        var desiredKeys = new HashSet<int>(desired.Select(d => d.Key));
        for (var i = HistoryItems.Count - 1; i >= 0; i--)
        {
            if (!desiredKeys.Contains(HistoryItems[i].Key))
                HistoryItems.RemoveAt(i);
        }
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < HistoryItems.Count && ReferenceEquals(HistoryItems[i], desired[i]))
                continue;

            var found = -1;
            for (var j = i + 1; j < HistoryItems.Count; j++)
            {
                if (ReferenceEquals(HistoryItems[j], desired[i]))
                {
                    found = j;
                    break;
                }
            }
            if (found >= 0)
                HistoryItems.Move(found, i);
            else
                HistoryItems.Insert(i, desired[i]);
        }

        StatusText = string.Format(Localizer.Get("EntriesCountFormat"), HistoryItems.Count);

        // Only genuinely-new rows need their titles/thumbnails fetched.
        QueueMetadata(newItems);
    }

    private void QueueMetadata(List<HistoryItemViewModel> items)
    {
        if (items.Count == 0)
            return;
        if (_isLoadingMetadata)
        {
            _pendingMetadata.AddRange(items);
            return;
        }
        LoadMetadata(items);
    }

    private void LoadMetadata(List<HistoryItemViewModel> items)
    {
        _isLoadingMetadata = true;

        _ = Task.Run(async () =>
        {
            foreach (var groupedItems in items.GroupBy(h => h.Id))
            {
                (string? DisplayTitle, string? ThumbnailUrl)? metadata = null;
                foreach (var item in groupedItems)
                {
                    if (metadata == null)
                    {
                        metadata = await item.LoadMetadataAsync();
                    }
                    else
                    {
                        item.SetMetadata(metadata.Value.DisplayTitle, metadata.Value.ThumbnailUrl);
                    }
                }
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _isLoadingMetadata = false;
                if (_pendingMetadata.Count > 0)
                {
                    var next = _pendingMetadata.ToList();
                    _pendingMetadata.Clear();
                    LoadMetadata(next);
                }
            });
        });
    }

    [RelayCommand]
    private async Task ClearAll()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var confirmed = await ConfirmWindow.ShowAsync(
            desktop.MainWindow!,
            Localizer.Get("ClearAllHistory"),
            Localizer.Get("ClearAllHistoryConfirm"));
        if (confirmed)
            DatabaseManager.ClearPlayHistory();
    }
}
