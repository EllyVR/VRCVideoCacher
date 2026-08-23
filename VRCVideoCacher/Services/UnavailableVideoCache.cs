using System.Collections.Concurrent;
using Serilog;

namespace VRCVideoCacher.Services;

/// <summary>
/// Remembers YouTube videos that came back "unavailable" (deleted, private, removed, terminated) so we
/// stop asking YouTube about them.
///
/// Some in-world video players re-request a dead video on a tight loop. Every request is a fresh yt-dlp
/// run against YouTube, and that volume of failing requests is exactly what trips YouTube's
/// "Sign in to confirm you're not a bot" check — the very thing this app exists to avoid. Once a video
/// is known-gone we short-circuit before any yt-dlp runs and hand the player a 403 so it (ideally) gives
/// up too.
///
/// The map is deliberately temporary (<see cref="Ttl"/>): "unavailable" is usually permanent, but a
/// region block or a botched extraction can be transient, so an entry expires and the video gets one
/// more real attempt rather than being blocked forever.
/// </summary>
public static class UnavailableVideoCache
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(UnavailableVideoCache));

    /// <summary>How long a video stays marked unavailable before we let it be retried.</summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private static readonly ConcurrentDictionary<string, DateTime> Expiry = new();

    /// <summary>
    /// yt-dlp phrases that mean the video is genuinely gone. Kept conservative: a bot-check or an
    /// age-gate is NOT unavailability (those recover once cookies are refreshed), and marking them would
    /// wrongly suppress a perfectly good video — so those are excluded in <see cref="IsUnavailabilityError"/>.
    /// </summary>
    private static readonly string[] Markers =
    [
        "Video unavailable",
        "This video is not available",
        "This video is no longer available",
        "no longer available",
        "has been removed",
        "removed by the uploader",
        "Private video",
        "This video is private",
        "account associated with this video has been terminated",
    ];

    /// <summary>
    /// Whether a yt-dlp error string indicates the video itself is gone (as opposed to a transient auth /
    /// bot-check failure, which must NOT be cached — that would blackhole a good video for 30 minutes).
    /// </summary>
    public static bool IsUnavailabilityError(string? error)
    {
        if (string.IsNullOrEmpty(error))
            return false;
        // "Sign in to confirm you're not a bot" and "...confirm your age" are transient, not unavailable.
        if (error.Contains("confirm you", StringComparison.OrdinalIgnoreCase))
            return false;
        return Markers.Any(m => error.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Records that this video is unavailable. No-ops for empty ids and the "live" sentinel.</summary>
    public static void Mark(string? videoId)
    {
        if (!IsCacheable(videoId))
            return;

        var isNew = !Expiry.ContainsKey(videoId!);
        Expiry[videoId!] = DateTime.UtcNow + Ttl;
        if (isNew)
            Log.Information("Marked YouTube video {VideoId} as unavailable for {Minutes} min; further " +
                            "requests for it will be refused without contacting YouTube", videoId, Ttl.TotalMinutes);
    }

    /// <summary>Whether this video is currently known-unavailable. Expired entries are pruned on access.</summary>
    public static bool IsUnavailable(string? videoId)
    {
        if (!IsCacheable(videoId))
            return false;
        if (!Expiry.TryGetValue(videoId!, out var expiresAt))
            return false;
        if (DateTime.UtcNow < expiresAt)
            return true;

        Expiry.TryRemove(videoId!, out _); // lapsed — give it another chance
        return false;
    }

    // "live" is a shared sentinel id (see YouTubeHandler), not a real video — caching it would blackhole
    // every livestream URL.
    private static bool IsCacheable(string? videoId) =>
        !string.IsNullOrEmpty(videoId) && videoId != "live";
}
