using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.ViewModels;

public partial class HistoryItemViewModel : ViewModelBase
{
    public DateTime Timestamp { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? Id { get; init; }
    public UrlType Type { get; init; }
    public string? Author { get; init; }
    public bool HasAuthor => !string.IsNullOrEmpty(Author);

    private readonly string? _title;

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

    public string? ThumbnailUrl => Type == UrlType.YouTube && Id?.Length == 11
        ? $"https://img.youtube.com/vi/{Id}/mqdefault.jpg"
        : null;

    public HistoryItemViewModel(History history, VideoInfoCache? meta)
    {
        Timestamp = history.Timestamp.ToLocalTime();
        Url = history.Url;
        Id = history.Id;
        Type = history.Type;
        _title = meta?.Title;
        Author = meta?.Author;
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
        DatabaseManager.OnPlayHistoryAdded += () => Task.Run(RefreshHistory);
        DatabaseManager.OnVideoInfoCacheUpdated += () => Task.Run(RefreshHistory);
        Task.Run(RefreshHistory);
    }

    [RelayCommand]
    private void Refresh() => Task.Run(RefreshHistory);

    private void RefreshHistory()
    {
        var history = DatabaseManager.GetPlayHistory(100);
        var ids = history.Select(h => h.Id).Where(id => id != null).Cast<string>();
        var meta = DatabaseManager.GetVideoInfoCacheByIds(ids);

        var items = history.Select(h =>
        {
            meta.TryGetValue(h.Id ?? string.Empty, out var info);
            return new HistoryItemViewModel(h, info);
        }).ToList();

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            HistoryItems.Clear();
            foreach (var item in items)
                HistoryItems.Add(item);
            StatusText = $"{HistoryItems.Count} entries";
        });
    }
}
