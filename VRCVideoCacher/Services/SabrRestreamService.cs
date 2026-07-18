using System.Collections.Concurrent;
using Serilog;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services.Sabr;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Services;

/// <summary>
/// Serves uncached YouTube videos to AVPro as a <b>seekable</b> HLS VOD, fetched live over SABR.
///
/// YouTube is moving to SABR delivery, which is not a plain URL AVPro can play. We speak the protocol
/// ourselves (<see cref="SabrClient"/>; yt-dlp is only a link extractor) and republish it as HLS.
///
/// The playlist is published complete, with ENDLIST, after one round-trip — the exact per-fragment
/// timeline rides at the head of each track (sidx for fMP4, Matroska Cues for WebM). That is what gives
/// AVPro a scrub bar, a correct duration, and working <c>SetTime()</c> — so VRChat's late-join/resync
/// works too. Media fills in behind the playlist, and seeking past the fetched region restarts the SABR
/// fetch at the target instead of waiting.
///
/// Superseded the earlier RTSP/MediaMTX design, which could never seek: a live RTSP stream has no
/// duration, so AVPro shows no scrub bar and <c>SetTime()</c> is a no-op.
/// </summary>
public static class SabrRestreamService
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(SabrRestreamService));

    /// <summary>
    /// Sessions are reaped after this long with nobody watching. "Watching" cannot be inferred from
    /// getvideo requests: VRChat asks us for a URL exactly once and then pulls the media directly, so a
    /// happily-playing stream would look idle forever. Liveness comes from the transport — the HLS
    /// requests themselves (<see cref="EnsureAsync"/> touches the session).
    /// </summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Live sessions are reaped far sooner, because silence means much more here and costs much more.
    ///
    /// A live player MUST re-fetch the playlist every segment (~2s) to discover new ones, so a gap of
    /// this length is unambiguous — whereas a VOD player legitimately goes quiet for minutes once it has
    /// buffered ahead, which is why that timeout stays long. And the cost of guessing wrong is
    /// asymmetric: an unwatched live fetch keeps pulling the broadcast at full bitrate forever, and
    /// unlike VOD none of it is cached, so every byte is wasted.
    ///
    /// Coming back is cheap and correct: a new session starts at the live edge, which is where a
    /// returning viewer wants to be anyway.
    /// </summary>
    private static readonly TimeSpan LiveIdleTimeout = TimeSpan.FromSeconds(20);

    private static readonly ConcurrentDictionary<string, ISabrSession> Sessions = new();

    /// <summary>
    /// What each live session was started for, so a session that ends WITHOUT having produced the cache
    /// file can fall back to a normal download. See <see cref="EnsureCachedAsync"/>.
    /// </summary>
    private static readonly ConcurrentDictionary<string, VideoInfo> SessionVideos = new();

    /// <summary>
    /// Starts in flight. VRChat fires several getvideo requests for the same video within a second, and a
    /// session is only published at the end of a multi-second start — without this they each spawn a
    /// pipeline and fight over the same directory.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Lazy<Task<ISabrSession?>>> Starting = new();

    /// <summary>Directory HLS sessions are written to; served by EmbedIO at <c>/hls</c>.</summary>
    public static string HlsRootPath { get; } = Path.Join(Program.DataPath, "hls");

    /// <summary>
    /// Whether the streamed media can double as the cached copy, so the video is fetched once instead of
    /// twice. Only when the two resolutions agree: <c>SabrMaxResolution</c> governs STREAMING and
    /// <c>CacheYouTubeMaxResolution</c> governs the CACHE, and they are deliberately separate settings —
    /// converging when they differ would silently cache at the streaming resolution. When they differ,
    /// <see cref="YTDL.VideoDownloader"/> downloads the cache copy separately, as before.
    /// </summary>
    public static bool CacheConverges =>
        ConfigManager.Config is
        {
            SabrRestreamEnabled: true,
            CacheYouTube: true,
        } && ConfigManager.Config.SabrMaxResolution == ConfigManager.Config.CacheYouTubeMaxResolution;

    static SabrRestreamService()
    {
        Directory.CreateDirectory(HlsRootPath);
        CleanOrphanedSessions();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ShutdownAll();
        Task.Run(ReaperLoop);
    }

    /// <summary>
    /// Sessions are ephemeral — the raw fragments and muxed segments are a cache of one playback, not
    /// data worth keeping. A session directory holds the ENTIRE video (an hour of 4K is several GB), so
    /// anything left behind by a crash or a hard kill would sit there forever: the reaper only ever sees
    /// sessions this process started.
    /// </summary>
    private static void CleanOrphanedSessions()
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(HlsRootPath))
            {
                try { Directory.Delete(dir, true); }
                catch (Exception ex) { Log.Debug(ex, "Could not remove orphaned SABR session {Dir}", dir); }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to clean orphaned SABR sessions in {Path}", HlsRootPath);
        }
    }

    /// <summary>
    /// Start (or reuse) a session for an uncached YouTube video and return the URL AVPro should play, or
    /// null if restreaming is disabled or unavailable.
    /// </summary>
    public static async Task<string?> TryGetRestreamUrlAsync(VideoInfo videoInfo)
    {
        if (!ConfigManager.Config.SabrRestreamEnabled)
            return null;
        if (videoInfo.UrlType != UrlType.YouTube || string.IsNullOrEmpty(videoInfo.VideoId))
            return null;

        var videoId = videoInfo.VideoId;

        if (Sessions.TryGetValue(videoId, out var existing))
        {
            existing.Touch();
            return existing.PlaybackUrl;
        }

        var starter = Starting.GetOrAdd(videoId, _ => new Lazy<Task<ISabrSession?>>(
            () => StartSessionAsync(videoInfo), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var session = await starter.Value;
            if (session == null)
                return null;

            Sessions[videoId] = session;
            SessionVideos[videoId] = videoInfo;
            return session.PlaybackUrl;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start SABR HLS session for {VideoId}", videoId);
            return null;
        }
        finally
        {
            Starting.TryRemove(videoId, out _);
        }
    }

    /// <summary>
    /// Whether the running session for this video is a live broadcast. Callers use it to skip anything
    /// that only makes sense for a finite video — above all queueing a cache download, which for a
    /// livestream would never finish.
    /// </summary>
    public static bool IsLiveSession(string videoId) =>
        Sessions.TryGetValue(videoId, out var session) && session is SabrLiveSession;

    private static async Task<ISabrSession?> StartSessionAsync(VideoInfo videoInfo)
    {
        var videoId = videoInfo.VideoId;

        // SabrMaxResolution is the STREAMING knob. CacheYouTubeMaxResolution is for the cache download
        // only — do not reuse it here.
        var maxHeight = Math.Max(360, ConfigManager.Config.SabrMaxResolution);
        var baseUrl = ConfigManager.Config.YtdlpWebServerUrl.TrimEnd('/');
        var playbackUrl = $"{baseUrl}/hls/{videoId}/{HlsPlaylist.PlaylistName}";

        var cookies = ConfigManager.Config.YtdlpUseCookies && File.Exists(YtdlManager.CookiesPath)
            ? YtdlManager.CookiesPath
            : null;

        // Extract once, here, because liveness is only knowable from the extraction — and a live
        // broadcast needs an entirely different session. The result is handed to whichever session we
        // build, so this costs no extra yt-dlp run.
        // The one yt-dlp we ship is the SABR-capable build, used here purely as a link extractor (-J).
        var source = await SabrExtractor.ExtractAsync(videoInfo.VideoUrl, maxHeight, YtdlManager.YtdlPath,
            cookies, Log);

        if (source.IsLive)
        {
            if (!ConfigManager.Config.SabrLiveEnabled)
            {
                Log.Information("{VideoId} is a livestream and SabrLiveEnabled is off; using the normal path",
                    videoId);
                return null;
            }

            return await SabrLiveSession.StartAsync(videoId, source, HlsRootPath, playbackUrl,
                YtdlManager.FfmpegPath, Log);
        }

        var session = await SabrHlsSession.StartAsync(videoId, videoInfo.VideoUrl, maxHeight, HlsRootPath,
            playbackUrl, YtdlManager.YtdlPath, YtdlManager.FfmpegPath, cookies, Log, source);

        if (CacheConverges)
            session.OnFullyFetched = WriteCacheFileAsync;

        return session;
    }

    /// <summary>
    /// Turns the fully-fetched session into the cached file, so the video is fetched once rather than
    /// streamed and then downloaded all over again.
    /// </summary>
    private static async Task WriteCacheFileAsync(SabrHlsSession session)
    {
        var videoId = session.VideoId;

        var maxLengthMinutes = ConfigManager.Config.CacheYouTubeMaxLength;
        if (maxLengthMinutes > 0 && session.DurationMs > maxLengthMinutes * 60_000L)
        {
            Log.Information("Not caching {VideoId}: {Duration:0} min exceeds the {Max} min cache limit",
                videoId, session.DurationMs / 60_000.0, maxLengthMinutes);
            return;
        }

        // H.264/VP9 + Opus in MP4 — the same combination AVPro already plays in our HLS segments, so no
        // separate AAC fetch is needed. GetCachedFile falls back to .mp4 for the avpro=true case too.
        var fileName = $"{videoId}.mp4";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (File.Exists(filePath))
            return;

        try
        {
            var temp = filePath + ".part";
            await session.WriteCompleteFileAsync(temp);
            File.Move(temp, filePath, overwrite: true);

            CacheManager.AddToCache(fileName);
            Log.Information("Cached {VideoId} from the streamed fragments (no second download)", videoId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to write the cache file for {VideoId}", videoId);
        }
    }

    /// <summary>
    /// Serves an <c>/hls</c> request: builds the segment (fetching or seeking as needed) so it exists
    /// before the static file module tries to send it. Also the session's only liveness signal.
    /// </summary>
    public static async Task EnsureAsync(string requestedPath)
    {
        // requestedPath is relative to the /hls base route, e.g. "/<videoId>/seg_00042.m4s".
        var parts = requestedPath.Trim('/').Split('/');
        if (parts.Length != 2)
            return;

        if (!Sessions.TryGetValue(parts[0], out var session))
            return;

        try
        {
            await session.EnsureAsync(parts[1]);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SABR HLS request failed: {Path}", requestedPath);
        }
    }

    /// <summary>
    /// The current live playlist, or null when this is not a live session's playlist request.
    ///
    /// Live playlists are served by us rather than by EmbedIO's static file module, because that module
    /// emits <c>ETag</c>/<c>Last-Modified</c> and honours <c>If-None-Match</c>. A live playlist is
    /// rewritten every couple of seconds, and two rewrites inside one filesystem timestamp tick would
    /// let it answer <c>304 Not Modified</c> with a stale playlist — which stalls playback with no error
    /// anywhere. A finite VOD playlist never changes, so it is happily left to the file module.
    /// </summary>
    public static async Task<string?> TryGetLivePlaylistAsync(string requestedPath)
    {
        var parts = requestedPath.Trim('/').Split('/');
        if (parts.Length != 2 || parts[1] != HlsPlaylist.PlaylistName)
            return null;
        if (!Sessions.TryGetValue(parts[0], out var session) || session is not SabrLiveSession live)
            return null;

        try
        {
            await live.EnsureAsync(HlsPlaylist.PlaylistName);
            return await File.ReadAllTextAsync(Path.Combine(HlsRootPath, parts[0], HlsPlaylist.PlaylistName));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to serve live playlist: {Path}", requestedPath);
            return null;
        }
    }

    /// <summary>
    /// Guarantees the video ends up cached even when the streamed fetch could not supply it.
    ///
    /// A session only writes the cache file if it holds EVERY fragment — and seeking cancels the fill, so
    /// after someone scrubs around, the fetched fragments are scattered (0-40, 296-314, 500-530…) and
    /// there is no complete copy to save. That is correct (we must never write a truncated file), but on
    /// its own it would mean a seeked video is silently never cached at all, since convergence stops
    /// APIController from queueing the usual download. So on teardown, if the cache file isn't there,
    /// fall back to a normal download — a second fetch, but only in the case where we genuinely can't
    /// avoid it.
    /// </summary>
    private static void EnsureCached(VideoInfo videoInfo)
    {
        if (!CacheConverges)
            return; // APIController already queued the download

        var fileName = $"{videoInfo.VideoId}.mp4";
        if (File.Exists(Path.Join(CacheManager.CachePath, fileName)))
            return; // the session produced it from the streamed fragments

        Log.Information("SABR session for {VideoId} ended without a complete copy (seeking leaves gaps); " +
                        "queueing a normal download so it still gets cached", videoInfo.VideoId);
        VideoDownloader.QueueDownload(videoInfo);
    }

    private static async Task ReaperLoop()
    {
        while (true)
        {
            // Polled often, because the live timeout is short and every extra second of an unwatched
            // live session is bandwidth spent on nothing.
            await Task.Delay(TimeSpan.FromSeconds(5));
            foreach (var (id, session) in Sessions)
            {
                var isLive = session is SabrLiveSession;

                // A finished broadcast has nothing left to serve, so it need not wait out the idle
                // timeout — but give it a grace period so the player can drain what it already has.
                var ended = session is SabrLiveSession { Ended: true } &&
                            session.IdleFor > TimeSpan.FromSeconds(10);

                if (!ended && session.IdleFor <= (isLive ? LiveIdleTimeout : IdleTimeout))
                    continue;

                Log.Information("Tearing down SABR session for {VideoId} ({Reason}, idle {Idle:g})",
                    id, ended ? "broadcast ended" : "idle", session.IdleFor);
                Sessions.TryRemove(id, out _);

                // Do this BEFORE Dispose: it deletes the session directory, and with it any chance of
                // telling whether we actually got the video cached.
                // A live broadcast is never cached and has no "complete copy" to fall back to — queueing
                // a download for one would hand the single serial download worker a job that never ends.
                if (SessionVideos.TryRemove(id, out var videoInfo) && session is not SabrLiveSession)
                    EnsureCached(videoInfo);

                session.Dispose();
            }
        }
    }

    private static void ShutdownAll()
    {
        foreach (var (id, session) in Sessions)
        {
            Sessions.TryRemove(id, out _);
            SessionVideos.TryRemove(id, out _);
            session.Dispose(); // app is exiting; no point queueing a download
        }
    }
}
