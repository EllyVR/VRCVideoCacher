using System.Collections.Concurrent;
using System.Diagnostics;
using Serilog;

namespace VRCVideoCacher.Services.Sabr;

/// <summary>
/// One video, served to AVPro as a seekable HLS VOD while it is still being fetched.
///
/// Two layers:
///   * SABR fetches YouTube's own fragments and caches them raw — no ffmpeg, so segment boundaries are
///     exact by construction.
///   * A requested HLS segment is muxed on demand from exactly the fragments covering it (see
///     <see cref="SabrSegmentMuxer"/>), so it always matches the playlist.
///
/// The playlist goes out complete, with ENDLIST, after ONE round-trip — the segment index rides at the
/// head of each track. That matters: AVPro will not seek a playlist without ENDLIST, it treats it as a
/// livestream and shows no scrub bar. Waiting for the whole fetch would mean minutes of startup.
///
/// Seeking past the fetched region restarts the SABR fetch at the target rather than waiting for the
/// sequential fill to crawl there.
/// </summary>
internal sealed class SabrHlsSession : ISabrSession
{
    private const string RawVideoDir = "raw_v";
    private const string RawAudioDir = "raw_a";

    /// <summary>How far ahead of the fill a request may be before it counts as a seek rather than buffering.</summary>
    private const int SeekThresholdSegments = 4;

    /// <summary>
    /// How many segments to mux ahead of the one being served. A cold 4K build costs 0.25-1.6s (two
    /// ffmpeg passes over ~20MB of fragments) versus ~2ms for one already on disk, and a spike is enough
    /// to underrun the player's buffer.
    /// </summary>
    private const int PrebuildAhead = 3;

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FragmentWaitTimeout = TimeSpan.FromSeconds(30);

    private readonly string _videoId;
    private readonly string _dir;
    private readonly SegmentIndex _videoIndex;
    private readonly SegmentIndex _audioIndex;
    private readonly SabrSource _source;
    private readonly Func<CancellationToken, Task<SabrSource>> _reload;
    private readonly SabrSegmentMuxer _muxer;
    private readonly ILogger _log;

    private readonly SemaphoreSlim _fillLock = new(1, 1);
    private readonly ConcurrentDictionary<int, Lazy<Task>> _building = new();

    private CancellationTokenSource _fillCts = new();
    private volatile int _videoFillStart;

    /// <summary>
    /// How far the CURRENT fill has actually got. Deliberately NOT "the highest fragment on disk": after
    /// a few seeks the fetched fragments are scattered (0-40, 296-314, 500-530…), so the maximum index on
    /// disk says nothing about where the running fetch is. Using it made the seek detector believe every
    /// segment was about to arrive, so it never seeked — it waited for the fill to crawl there and timed out.
    /// </summary>
    private volatile int _fillHead;

    private DateTime _lastAccess = DateTime.UtcNow;

    public string PlaybackUrl { get; }
    public string VideoId => _videoId;
    public TimeSpan IdleFor => DateTime.UtcNow - _lastAccess;
    public long DurationMs => _videoIndex.TotalDurationMs;

    /// <summary>
    /// Raised once the session holds every fragment of the video, so the fetched media can be turned into
    /// the cached copy instead of downloading the whole thing a second time.
    /// </summary>
    public Func<SabrHlsSession, Task>? OnFullyFetched;

    public void Touch() => _lastAccess = DateTime.UtcNow;

    private SabrHlsSession(string videoId, string dir, string playbackUrl, SegmentIndex videoIndex,
        SegmentIndex audioIndex, SabrSource source, Func<CancellationToken, Task<SabrSource>> reload,
        SabrSegmentMuxer muxer, ILogger log)
    {
        _videoId = videoId;
        _dir = dir;
        _videoIndex = videoIndex;
        _audioIndex = audioIndex;
        _source = source;
        _reload = reload;
        _muxer = muxer;
        _log = log;
        PlaybackUrl = playbackUrl;
    }

    public static async Task<SabrHlsSession> StartAsync(string videoId, string videoUrl, int maxHeight,
        string rootDir, string playbackUrl, string ytdlpPath, string ffmpegPath, string? cookiesPath, ILogger log,
        SabrSource? preExtracted = null)
    {
        var dir = Path.Combine(rootDir, videoId);
        if (Directory.Exists(dir))
            TryDelete(dir);
        Directory.CreateDirectory(Path.Combine(dir, RawVideoDir));
        Directory.CreateDirectory(Path.Combine(dir, RawAudioDir));

        var sw = Stopwatch.StartNew();

        Task<SabrSource> Extract(CancellationToken ct) =>
            SabrExtractor.ExtractAsync(videoUrl, maxHeight, ytdlpPath, cookiesPath, log, ct);

        // The caller may already have extracted (it has to, to tell live from VOD) — reuse it rather
        // than paying for a second yt-dlp run on every play.
        var source = preExtracted ?? await Extract(CancellationToken.None);

        // One round-trip: both tracks' indexes ride at the head of their streams, so the whole timeline
        // is known before any media is fetched.
        var (videoIndex, audioIndex) = await ProbeIndexesAsync(source, Extract, log);

        var session = new SabrHlsSession(videoId, dir, playbackUrl, videoIndex, audioIndex, source, Extract,
            new SabrSegmentMuxer(ffmpegPath, log), log);

        await File.WriteAllTextAsync(Path.Combine(dir, HlsPlaylist.PlaylistName), HlsPlaylist.Build(videoIndex));
        log.Information("SABR HLS ready for {VideoId} in {Elapsed:0.0}s: {Count} segments, {Duration:0.0}s",
            videoId, sw.Elapsed.TotalSeconds, videoIndex.Count, videoIndex.TotalDurationMs / 1000.0);

        await session.StartFillAsync(0);
        return session;
    }

    /// <summary>One SABR request, just far enough to read both tracks' indexes off the head of the stream.</summary>
    private static async Task<(SegmentIndex Video, SegmentIndex Audio)> ProbeIndexesAsync(SabrSource source,
        Func<CancellationToken, Task<SabrSource>> reload, ILogger log)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var cts = new CancellationTokenSource(StartTimeout);
        var probe = new SabrClient(http, source, log, reload);

        var fetch = probe.DownloadAsync(Stream.Null, Stream.Null, 0, ct: cts.Token);
        try
        {
            var video = await probe.SegmentIndexAsync.WaitAsync(cts.Token);
            var audio = await probe.AudioSegmentIndexAsync.WaitAsync(cts.Token);
            await cts.CancelAsync();
            return (video, audio);
        }
        finally
        {
            try { await fetch; } catch { /* cancelled, as intended */ }
        }
    }

    // region: fetching raw fragments

    /// <summary>
    /// Restarts the SABR fetch at <paramref name="fromMs"/>, abandoning the previous fill. That is what a
    /// seek costs: one round-trip. Fragments already on disk are kept, so seeking back into fetched
    /// territory is free.
    /// </summary>
    private async Task StartFillAsync(long fromMs)
    {
        await _fillLock.WaitAsync();
        try
        {
            await _fillCts.CancelAsync();
            _fillCts = new CancellationTokenSource();
            var ct = _fillCts.Token;

            _videoFillStart = _videoIndex.IndexAt(fromMs);
            _fillHead = _videoFillStart - 1;

            _log.Debug("SABR {VideoId}: filling from {Start:0.0}s (video fragment {Fragment})",
                _videoId, fromMs / 1000.0, _videoFillStart);

            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            var client = new SabrClient(http, _source, _log, _reload) { OnFragment = WriteFragmentAsync };

            _ = Task.Run(async () =>
            {
                try
                {
                    await client.DownloadAsync(Stream.Null, Stream.Null, fromMs, ct: ct);
                    _log.Debug("SABR {VideoId}: fill complete", _videoId);

                    // A fill that ran to the end may have completed the set — but only if it started at
                    // the beginning, or an earlier fill already covered the gap. HasAllFragments checks.
                    if (OnFullyFetched is { } onFullyFetched && HasAllFragments())
                        await onFullyFetched(this);
                }
                catch (OperationCanceledException) { /* superseded by a seek */ }
                catch (Exception ex)
                {
                    _log.Error(ex, "SABR {VideoId}: fill from {Start:0.0}s failed", _videoId, fromMs / 1000.0);
                }
                finally
                {
                    http.Dispose();
                }
            }, ct);
        }
        finally
        {
            _fillLock.Release();
        }
    }

    /// <summary>Caches a fragment exactly as YouTube sent it. SABR sequence numbers are 1-based.</summary>
    private async Task WriteFragmentAsync(SabrSegment segment, byte[] data)
    {
        var index = segment.IsInit ? -1 : (int)segment.SequenceNumber - 1;
        var path = RawPath(segment.IsVideo, index);

        if (!File.Exists(path))
        {
            var temp = path + ".part";
            await File.WriteAllBytesAsync(temp, data);
            try { File.Move(temp, path, overwrite: true); }
            catch (IOException) { try { File.Delete(temp); } catch { /* raced */ } }
        }

        // Track the running fill's real progress. SABR delivers forward-contiguously from the seek point,
        // so this is monotonic within a fill, and it is reset when a seek starts a new one.
        if (segment.IsVideo && index > _fillHead)
            _fillHead = index;
    }

    private string RawPath(bool isVideo, int fragment) =>
        Path.Combine(_dir, isVideo ? RawVideoDir : RawAudioDir, fragment < 0 ? "init.bin" : $"{fragment:D5}.bin");

    // endregion

    /// <summary>Serves a request, building the segment (and fetching what it needs) if necessary.</summary>
    public async Task EnsureAsync(string fileName)
    {
        Touch();

        if (fileName == HlsPlaylist.PlaylistName)
            return; // we wrote it ourselves

        if (fileName == HlsPlaylist.InitName)
        {
            // The init segment is a by-product of muxing, so make sure at least one segment exists.
            await BuildSegmentAsync(0);
            return;
        }

        if (!TryParseSegment(fileName, out var segment))
            return;

        await BuildSegmentAsync(segment);
        StartPrebuild(segment);
    }

    private void StartPrebuild(int from)
    {
        for (var i = from + 1; i <= from + PrebuildAhead && i < _videoIndex.Count; i++)
        {
            // A prebuild must NEVER move the fetch: it is speculative, so if the fragments aren't there
            // yet, skip it rather than yanking the SABR stream away from the playhead.
            var segment = i;
            _ = Task.Run(() => BuildSegmentAsync(segment, allowSeek: false));
        }
    }

    private async Task BuildSegmentAsync(int segment, bool allowSeek = true)
    {
        var path = Path.Combine(_dir, HlsPlaylist.SegmentName(segment));

        // Twice at most: a speculative prebuild may have run first and skipped because the fragments
        // weren't fetched yet. Its no-op result must NOT be what a real request gets — drop the
        // single-flight entry and build again, this time allowed to move the fetch.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (File.Exists(path))
                return;

            var building = _building.GetOrAdd(segment, s => new Lazy<Task>(
                () => BuildSegmentCoreAsync(s, allowSeek), LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                await building.Value;
            }
            finally
            {
                // Never cache a build that produced nothing (skipped prebuild, or a failure).
                if (!File.Exists(path))
                    _building.TryRemove(segment, out _);
            }

            if (File.Exists(path) || !allowSeek)
                return;
        }
    }

    private async Task BuildSegmentCoreAsync(int segment, bool allowSeek)
    {
        var startMs = _videoIndex.StartMsOf(segment);
        var endMs = startMs + _videoIndex.DurationsMs[segment];

        // Audio fragments are longer than the video ones and straddle the segment, so take every one that
        // overlaps it; the muxer trims them to the exact span.
        var firstAudio = _audioIndex.IndexAt(startMs);
        var lastAudio = _audioIndex.IndexAt(Math.Max(startMs, endMs - 1));

        if (!await EnsureFetchedAsync(segment, startMs, firstAudio, lastAudio, allowSeek))
            return; // speculative prebuild and the media isn't here yet; the real request will do it

        var videoInit = await File.ReadAllBytesAsync(RawPath(true, -1));
        var audioInit = await File.ReadAllBytesAsync(RawPath(false, -1));
        var videoFragment = await File.ReadAllBytesAsync(RawPath(true, segment));

        var audioFragments = new List<byte[]>();
        for (var i = firstAudio; i <= lastAudio; i++)
            audioFragments.Add(await File.ReadAllBytesAsync(RawPath(false, i)));

        // mfhd sequence numbers are 1-based and must increase across the movie's fragments.
        await _muxer.MuxAsync(videoInit, videoFragment, audioInit, audioFragments, startMs, endMs, segment + 1,
            Path.Combine(_dir, HlsPlaylist.SegmentName(segment)),
            Path.Combine(_dir, HlsPlaylist.InitName));
    }

    /// <summary>
    /// Makes sure the raw fragments this segment needs are on disk, seeking the fetch if not. Returns
    /// false when a speculative prebuild found the media missing (it never seeks).
    /// </summary>
    private async Task<bool> EnsureFetchedAsync(int segment, long startMs, int firstAudio, int lastAudio,
        bool allowSeek)
    {
        bool Have()
        {
            if (!File.Exists(RawPath(true, -1)) || !File.Exists(RawPath(false, -1))) return false;
            if (!File.Exists(RawPath(true, segment))) return false;
            for (var i = firstAudio; i <= lastAudio; i++)
                if (!File.Exists(RawPath(false, i)))
                    return false;
            return true;
        }

        if (Have())
            return true;
        if (!allowSeek)
            return false;

        // Behind the current fill, or too far ahead of it to arrive soon: that is a seek, so move the
        // fetch rather than making the player wait for the sequential fill to reach it.
        if (segment < _videoFillStart || segment > _fillHead + SeekThresholdSegments)
        {
            _log.Information("SABR {VideoId}: seek to segment {Segment} ({Start:0.0}s), fill at {Head}",
                _videoId, segment, startMs / 1000.0, _fillHead);
            await StartFillAsync(startMs);
        }

        var deadline = DateTime.UtcNow + FragmentWaitTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Have())
                return true;
            await Task.Delay(100);
        }

        throw new SabrException($"Timed out fetching the fragments for segment {segment}");
    }

    /// <summary>True once every fragment of both tracks is on disk. Seeks leave gaps, so this is not implied
    /// by a fill reaching the end.</summary>
    private bool HasAllFragments()
    {
        if (!File.Exists(RawPath(true, -1)) || !File.Exists(RawPath(false, -1)))
            return false;
        for (var i = 0; i < _videoIndex.Count; i++)
            if (!File.Exists(RawPath(true, i)))
                return false;
        for (var i = 0; i < _audioIndex.Count; i++)
            if (!File.Exists(RawPath(false, i)))
                return false;
        return true;
    }

    /// <summary>
    /// Writes the fully-fetched video out as a single playable file. This is the cached copy, built from
    /// the fragments we already streamed — so the video is fetched once, not twice.
    /// </summary>
    public async Task WriteCompleteFileAsync(string outputPath, CancellationToken ct = default)
    {
        var videoTrack = Path.Combine(_dir, "complete_v.tmp");
        var audioTrack = Path.Combine(_dir, "complete_a.tmp");
        try
        {
            await ConcatTrackAsync(videoTrack, isVideo: true, _videoIndex.Count, ct);
            await ConcatTrackAsync(audioTrack, isVideo: false, _audioIndex.Count, ct);
            await _muxer.MuxCompleteAsync(videoTrack, audioTrack, outputPath, ct);
        }
        finally
        {
            foreach (var path in new[] { videoTrack, audioTrack })
                try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    /// <summary>init + every fragment in order is a complete, playable stream — exactly how yt-dlp builds one.</summary>
    private async Task ConcatTrackAsync(string path, bool isVideo, int count, CancellationToken ct)
    {
        await using var output = File.Create(path);
        await using (var init = File.OpenRead(RawPath(isVideo, -1)))
            await init.CopyToAsync(output, ct);

        for (var i = 0; i < count; i++)
        {
            await using var fragment = File.OpenRead(RawPath(isVideo, i));
            await fragment.CopyToAsync(output, ct);
        }
    }

    private static bool TryParseSegment(string fileName, out int index)
    {
        index = -1;
        return fileName.StartsWith("seg_", StringComparison.Ordinal)
               && fileName.EndsWith(".m4s", StringComparison.Ordinal)
               && int.TryParse(fileName.AsSpan(4, fileName.Length - 8), out index);
    }

    public void Dispose()
    {
        try { _fillCts.Cancel(); } catch { /* already gone */ }
        TryDelete(_dir);
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* best effort */ }
    }
}
