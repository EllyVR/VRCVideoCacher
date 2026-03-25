using System.Collections.Concurrent;

namespace VRCVideoCacher.YTDL;

/// <summary>
/// Tracks videos currently being streamed by VRChat so that cache downloads
/// can be deferred to avoid bandwidth contention.
/// </summary>
public static class ActiveStreamTracker
{
    private static readonly ConcurrentDictionary<string, DateTime> ActiveStreams = new();

    /// <summary>
    /// Mark a video as actively streaming. Called when a non-cached URL is
    /// returned to VRChat for direct playback.
    /// </summary>
    public static void MarkStreaming(string videoId)
    {
        ActiveStreams[videoId] = DateTime.UtcNow;
    }

    /// <summary>
    /// Clear the streaming flag for a video. Called when a new video request
    /// comes in (the previous video is no longer streaming).
    /// </summary>
    public static void ClearStreaming(string videoId)
    {
        ActiveStreams.TryRemove(videoId, out _);
    }

    /// <summary>
    /// Clear all active streams. Called when a new video starts playing,
    /// since VRChat only plays one video at a time.
    /// </summary>
    public static void ClearAll()
    {
        ActiveStreams.Clear();
    }

    /// <summary>
    /// Returns true if any video is currently being streamed.
    /// Entries older than 30 minutes are considered stale and ignored.
    /// </summary>
    public static bool IsAnyStreaming()
    {
        CleanupStale();
        return !ActiveStreams.IsEmpty;
    }

    /// <summary>
    /// Returns true if the specific video is currently being streamed.
    /// </summary>
    public static bool IsStreaming(string videoId)
    {
        CleanupStale();
        return ActiveStreams.ContainsKey(videoId);
    }

    private static void CleanupStale()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        foreach (var kvp in ActiveStreams)
        {
            if (kvp.Value < cutoff)
                ActiveStreams.TryRemove(kvp.Key, out _);
        }
    }
}
