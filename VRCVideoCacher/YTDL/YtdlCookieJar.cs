namespace VRCVideoCacher.YTDL;

/// <summary>
/// Serialises every yt-dlp invocation that passes <c>--cookies</c>.
///
/// yt-dlp does not merely READ the cookie jar — on exit it writes the whole file back, because YouTube
/// rotates session tokens (<c>__Secure-1PSIDTS</c>, <c>SIDCC</c>, …) on every request and yt-dlp
/// persists the refreshed ones. Two yt-dlp processes sharing the file therefore each load the jar, each
/// receive a DIFFERENT rotated token, and each rewrite the whole file: last writer wins and the other
/// rotation is silently lost. The persisted session is then inconsistent, and YouTube answers the next
/// request with <i>"Sign in to confirm you're not a bot"</i>.
///
/// That is the exact error this app exists to prevent, and we were causing it ourselves — resolving a
/// URL for playback while the download queue was running hits the jar from two processes at once.
/// Confirmed empirically: a single <c>yt-dlp -J</c> run changes the file's contents.
///
/// yt-dlp is not the throughput bottleneck here, so serialising it is cheap. Do NOT replace this with
/// a per-process copy of the jar: that would throw away the rotated tokens and let the saved session
/// go stale.
/// </summary>
public static class YtdlCookieJar
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Hold this for the whole lifetime of a yt-dlp process, from Start until it has exited.</summary>
    public static async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                Gate.Release();
        }
    }
}
