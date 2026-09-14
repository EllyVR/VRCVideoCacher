using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Swan;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Services;

/// <summary>
/// Display priority of an activity. Higher value wins when several run at once, so the status bar
/// always shows the most attention-worthy thing (a tool download over a background stream, etc).
/// </summary>
public enum StatusCategory
{
    Downloading = 0,
    Streaming = 1,
    Provisioning = 2,
}

public enum StatusLevel
{
    Normal,
    Warning,
}

/// <summary>
/// A single in-progress activity. Producers wrap their work in <c>using var a = StatusService.Begin(...)</c>
/// and optionally call <see cref="Report"/> with a 0..1 fraction; disposing ends it.
/// </summary>
public sealed class StatusActivity : IDisposable
{
    internal Guid Id { get; } = Guid.NewGuid();
    internal long Seq { get; init; }
    public StatusCategory Category { get; }

    /// <summary>Optional stable identifier (e.g. a tool name) so consumers can tell which thing this is.</summary>
    public string? Key { get; init; }

    private volatile string _text;
    private double? _progress;
    private int _disposed;

    public string Text => _text;
    public double? Progress => _progress;

    internal StatusActivity(StatusCategory category, string text, double? progress)
    {
        Category = category;
        _text = text;
        _progress = progress;
    }

    /// <summary>Update the completion fraction (0..1). Throttled by the UI so calling it per chunk is fine.</summary>
    public void Report(double fraction)
    {
        _progress = Math.Clamp(fraction, 0, 1);
        StatusService.NotifyChanged();
    }

    /// <summary>Change the label mid-activity (e.g. a phase change).</summary>
    public void SetText(string text)
    {
        _text = text;
        StatusService.NotifyChanged();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        StatusService.End(this);
    }
}

/// <summary>Immutable snapshot the UI renders. Recomputed on every change.</summary>
public sealed record StatusSnapshot(
    string Text,
    StatusLevel Level,
    bool IsBusy,
    bool ShowBar,
    bool Indeterminate,
    double Progress,
    int ExtraCount)
{
    public static readonly StatusSnapshot Idle =
        new(string.Empty, StatusLevel.Normal, false, false, false, 0, 0);
}

/// <summary>
/// Central "what is the app doing right now" channel. Backend components publish activities; the status
/// bar shows the single highest-priority one plus a "(+N more)" count. Also surfaces the latest
/// warning/error briefly by piggy-backing on <see cref="LogService.OnLogEntry"/>.
/// Thread-safe; the UI marshals <see cref="Changed"/> onto the UI thread itself.
/// </summary>
public static partial class StatusService
{
    private static readonly ConcurrentDictionary<Guid, StatusActivity> Activities = new();
    private static long _seq;

    private sealed record FlashState(string Text, StatusLevel Level, DateTime Until);
    private static volatile FlashState? _flash;
    private static readonly TimeSpan FlashDuration = TimeSpan.FromSeconds(5);

    public static event Action? Changed;

    static StatusService()
    {
        // Auto-surface warnings/errors without hand-wiring every failure site.
        LogService.OnLogEntry += OnLogEntry;
    }

    /// <summary>Idempotent touch so the static constructor (and its log hook) runs even before the first activity.</summary>
    public static void Init() { }

    public static StatusActivity Begin(StatusCategory category, string text, double? progress = null, string? key = null)
    {
        var activity = new StatusActivity(category, text, progress)
        {
            Seq = Interlocked.Increment(ref _seq),
            Key = key,
        };
        Activities[activity.Id] = activity;
        NotifyChanged();
        return activity;
    }

    /// <summary>Keys of all currently-active activities that carry one (e.g. tools currently downloading).</summary>
    public static HashSet<string> ActiveKeys()
    {
        var keys = new HashSet<string>();
        foreach (var activity in Activities.Values)
        {
            if (activity.Key is { } key)
                keys.Add(key);
        }
        return keys;
    }

    /// <summary>Briefly show a transient message (warning color for warnings/errors), then revert.</summary>
    public static void Flash(string text, StatusLevel level)
    {
        _flash = new FlashState(text, level, DateTime.UtcNow + FlashDuration);
        NotifyChanged();
        _ = ExpireFlashAsync();
    }

    internal static void End(StatusActivity activity)
    {
        Activities.TryRemove(activity.Id, out _);
        NotifyChanged();
    }

    internal static void NotifyChanged() => Changed?.Invoke();

    public static StatusSnapshot Current
    {
        get
        {
            var flash = _flash;
            if (flash is not null && DateTime.UtcNow < flash.Until)
                return new StatusSnapshot(flash.Text, flash.Level, IsBusy: true,
                    ShowBar: false, Indeterminate: false, Progress: 0, ExtraCount: 0);

            if (Activities.IsEmpty)
                return StatusSnapshot.Idle;

            // Highest-priority category, then the most recently started within it.
            StatusActivity? top = null;
            foreach (var a in Activities.Values)
            {
                if (top is null || a.Category > top.Category ||
                    (a.Category == top.Category && a.Seq > top.Seq))
                    top = a;
            }
            if (top is null)
                return StatusSnapshot.Idle;

            var progress = top.Progress;
            return new StatusSnapshot(
                top.Text,
                StatusLevel.Normal,
                IsBusy: true,
                ShowBar: true,
                Indeterminate: progress is null,
                Progress: progress ?? 0,
                ExtraCount: Activities.Count - 1);
        }
    }

    private static async Task ExpireFlashAsync()
    {
        await Task.Delay(FlashDuration);
        NotifyChanged(); // let the bar revert once the flash has aged out
    }

    private static void OnLogEntry(LogEntry entry)
    {
        // entry.Level is the short code emitted by LogService ("WRN", "ERR", "FTL").
        if (entry.Level is "WRN" or "ERR" or "FTL")
            Flash(NewLineRegex().Replace(entry.Message, "").Truncate(200, "...") ?? "", StatusLevel.Warning);
    }

    [GeneratedRegex(@"\r\n|\r|\n")]
    private static partial Regex NewLineRegex();
}
