using System.Diagnostics;
using Serilog;

namespace VRCVideoCacher.Services.Sabr;

/// <summary>
/// One live broadcast, served to AVPro as a sliding-window HLS stream.
///
/// Deliberately a separate type from <see cref="SabrHlsSession"/> rather than a mode on it. Every load
/// bearing assumption differs — there is no index, no total, no end, no stable segment numbering and no
/// seeking — so folding live into the VOD session would mean branching in nearly every method, on the
/// code path that all normal YouTube playback depends on. The shared parts (<see cref="SabrClient"/>,
/// <see cref="SabrSegmentMuxer"/>, <see cref="HlsPlaylist"/>) are reused; the orchestration is not.
///
/// Two behaviours are worth knowing before changing anything here:
///
/// <b>The init segment is not sent separately.</b> A livestream never sets <c>is_init_segment</c>
/// (measured: 0 occurrences across both our client and yt-dlp's). Instead <c>ftyp+moov</c> — plus an
/// <c>emsg</c> — is prepended to the FIRST media fragment of each track. We split it back off at the
/// first <c>moof</c> and keep it as the init the muxer needs.
///
/// <b>Times stay absolute.</b> A broadcast's timeline is enormous (measured ~3,848,770s, a tfdt well
/// beyond 32 bits) and it is tempting to rebase it to zero. Do not: the muxer trims audio with
/// output-side <c>-ss</c>/<c>-to</c> against the media's own timestamps, so a rebased range matches
/// nothing and ffmpeg silently emits an empty audio track. HLS needs only durations, so the large
/// numbers are harmless.
/// </summary>
internal sealed class SabrLiveSession : ISabrSession
{
    private const string RawVideoDir = "raw_v";
    private const string RawAudioDir = "raw_a";
    private static readonly TimeSpan FragmentWaitTimeout = TimeSpan.FromSeconds(30);
    // How long to wait at startup for enough media to publish a first playlist.
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(45);

    private readonly string _videoId;
    private readonly string _dir;
    private readonly SabrSource _source;
    private readonly SabrSegmentMuxer _muxer;
    private readonly ILogger _log;

    private readonly LiveTimeline _video = new();
    private readonly LiveTimeline _audio = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _buildGate = new(1, 1);

    private DateTime _lastAccess = DateTime.UtcNow;
    private Task? _fill;

    public string PlaybackUrl { get; }
    public TimeSpan IdleFor => DateTime.UtcNow - _lastAccess;
    public bool Ended { get; private set; }

    /// <summary>
    /// Fragments received so far. Exposed so a test can prove the fetch actually STOPS on teardown —
    /// an unwatched live session that keeps pulling the broadcast is pure wasted bandwidth, and nothing
    /// else in the system would notice it happening.
    /// </summary>
    internal int FragmentsReceived;

    /// <summary>How many segments the playlist advertises. Short: this is a live edge, not a DVR.</summary>
    private int WindowSegments => Math.Max(3, ConfigManager.Config.SabrLiveWindowSegments);

    /// <summary>Keep a little more on disk than we advertise, so a slightly-behind player still finds it.</summary>
    private int RetainSegments => WindowSegments * 3;

    public void Touch() => _lastAccess = DateTime.UtcNow;

    private SabrLiveSession(string videoId, string dir, string playbackUrl, SabrSource source,
        SabrSegmentMuxer muxer, ILogger log)
    {
        _videoId = videoId;
        _dir = dir;
        _source = source;
        _muxer = muxer;
        _log = log;
        PlaybackUrl = playbackUrl;
    }

    public static async Task<SabrLiveSession> StartAsync(string videoId, SabrSource source, string rootDir,
        string playbackUrl, string ffmpegPath, ILogger log)
    {
        var dir = Path.Combine(rootDir, videoId);
        if (Directory.Exists(dir))
            TryDelete(dir);
        Directory.CreateDirectory(Path.Combine(dir, RawVideoDir));
        Directory.CreateDirectory(Path.Combine(dir, RawAudioDir));

        var session = new SabrLiveSession(videoId, dir, playbackUrl, source,
            new SabrSegmentMuxer(ffmpegPath, log), log);

        var sw = Stopwatch.StartNew();
        session._fill = Task.Run(() => session.FillLoopAsync(session._cts.Token));

        // Publish nothing until both tracks have enough to describe a playable window; an HLS player
        // that gets an empty playlist tends to give up rather than poll.
        var deadline = DateTime.UtcNow + StartTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (session._video.Count >= 2 && session._audio.Count >= 2)
                break;
            if (session._fill.IsFaulted)
                await session._fill; // surface the real failure
            await Task.Delay(200);
        }

        if (session._video.Count < 2)
        {
            session.Dispose();
            throw new SabrException($"Live SABR session for {videoId} produced no media within {StartTimeout.TotalSeconds:0}s");
        }

        await session.WritePlaylistAsync();
        log.Information("SABR LIVE ready for {VideoId} in {Elapsed:0.0}s: at sequence {Seq}, {Target}s segments",
            videoId, sw.Elapsed.TotalSeconds, session._video.LastSequence, source.TargetDurationSec);
        return session;
    }

    // region: fetch

    private async Task FillLoopAsync(CancellationToken ct)
    {
        try
        {
            // No timeout: a live fetch runs for as long as the broadcast does. The VOD session caps its
            // HttpClient at 15 minutes, which would silently kill a longer stream.
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var client = new SabrClient(http, _source, _log) { OnFragment = WriteFragmentAsync };

            await client.DownloadAsync(Stream.Null, Stream.Null, SabrClient.LiveEdgeStartMs, ct: ct);
            Ended = client.BroadcastEnded;
            if (Ended)
                _log.Information("SABR LIVE {VideoId}: broadcast ended", _videoId);
        }
        catch (OperationCanceledException)
        {
            // Session torn down; normal.
        }
        catch (Exception ex)
        {
            _log.Error(ex, "SABR LIVE {VideoId}: fill failed", _videoId);
        }
    }

    /// <summary>
    /// Stores a fragment and records it on the timeline. The first fragment of each track carries the
    /// init boxes ahead of the media, so it is split; every later one is media only.
    /// </summary>
    private async Task WriteFragmentAsync(SabrSegment segment, byte[] data)
    {
        Interlocked.Increment(ref FragmentsReceived);
        var timeline = segment.IsVideo ? _video : _audio;
        var initPath = RawPath(segment.IsVideo, null);
        var media = data;

        if (!File.Exists(initPath))
        {
            var moof = SabrSegmentMuxer.FindMoof(data);
            if (moof > 0)
            {
                await WriteAtomicAsync(initPath, data[..moof]);
                media = data[moof..];
                _log.Debug("SABR LIVE {VideoId}: took {Bytes}B of init from the first {Track} fragment",
                    _videoId, moof, segment.IsVideo ? "video" : "audio");
            }
            else
            {
                // No moof at all: nothing usable yet. Skip rather than poison the timeline.
                _log.Warning("SABR LIVE {VideoId}: first {Track} fragment carried no moof; skipping",
                    _videoId, segment.IsVideo ? "video" : "audio");
                return;
            }
        }

        await WriteAtomicAsync(RawPath(segment.IsVideo, segment.SequenceNumber), media);
        // Times stay ABSOLUTE (broadcast timeline, ~3.8e9 ms). Rebasing them to zero seems tidier and is
        // actively wrong: the muxer trims audio with output-side -ss/-to against the media's OWN
        // timestamps, so a rebased range selects nothing and ffmpeg emits an empty audio file. HLS only
        // needs durations, so the large numbers cost nothing.
        timeline.Append(segment.SequenceNumber, segment.StartMs, segment.DurationMs);

        if (segment.IsVideo)
            Evict();
    }

    /// <summary>Drops fragments that have slid out of the retained window. This is what bounds the disk.</summary>
    private void Evict()
    {
        var last = _video.LastSequence;
        if (last <= 0)
            return;

        var cutoff = last - RetainSegments;
        if (cutoff <= 0)
            return;

        foreach (var timeline in new[] { _video, _audio })
        {
            var isVideo = ReferenceEquals(timeline, _video);
            foreach (var sequence in timeline.EvictBefore(cutoff))
            {
                TryDeleteFile(RawPath(isVideo, sequence));
                if (isVideo)
                    TryDeleteFile(Path.Combine(_dir, HlsPlaylist.LiveSegmentName(sequence)));
            }
        }
    }

    // endregion
    // region: serve

    /// <summary>Materialises whatever the player just asked for.</summary>
    public async Task EnsureAsync(string fileName)
    {
        Touch();

        // Unlike VOD, the live playlist is NOT written once — it changes every couple of seconds, so it
        // is regenerated on every request.
        if (fileName == HlsPlaylist.PlaylistName)
        {
            await WritePlaylistAsync();
            return;
        }

        if (fileName == HlsPlaylist.InitName)
        {
            await EnsureInitAsync();
            return;
        }

        if (!TryParseSegmentName(fileName, out var sequence))
            return;

        await BuildSegmentAsync(sequence);
    }

    private async Task WritePlaylistAsync()
    {
        // Only advertise what both tracks cover: a segment needs its audio as well as its video, and the
        // two arrive independently.
        var playable = Math.Min(_video.LastSequence, _audio.LastSequence);
        var window = _video.Window(WindowSegments + 2)
            .Where(f => f.Sequence <= playable)
            .TakeLast(WindowSegments)
            .ToList();

        var playlist = HlsPlaylist.BuildLive(window, _source.TargetDurationSec);
        await WriteAtomicAsync(Path.Combine(_dir, HlsPlaylist.PlaylistName),
            System.Text.Encoding.UTF8.GetBytes(playlist));
    }

    /// <summary>
    /// The init AVPro fetches is ffmpeg's, not YouTube's — it describes the muxed audio+video track pair.
    /// Building any segment produces it as a side effect, so just build the oldest one we still hold.
    /// </summary>
    private async Task EnsureInitAsync()
    {
        if (File.Exists(Path.Combine(_dir, HlsPlaylist.InitName)))
            return;

        var window = _video.Window(WindowSegments);
        if (window.Count > 0)
            await BuildSegmentAsync(window[0].Sequence);
    }

    private async Task BuildSegmentAsync(long sequence)
    {
        var path = Path.Combine(_dir, HlsPlaylist.LiveSegmentName(sequence));
        if (File.Exists(path))
            return;

        await _buildGate.WaitAsync(_cts.Token);
        try
        {
            if (File.Exists(path))
                return;

            if (!await _video.WaitForAsync(sequence, FragmentWaitTimeout, _cts.Token))
            {
                _log.Warning("SABR LIVE {VideoId}: video fragment {Seq} never arrived (window is {First}..{Last})",
                    _videoId, sequence, _video.FirstSequence, _video.LastSequence);
                return;
            }

            if (!_video.TryGet(sequence, out var fragment))
                return;

            var startMs = fragment.StartMs;
            var endMs = startMs + fragment.DurationMs;

            // Audio fragments run alongside the video ones (measured: same sequence numbers and near
            // identical start times), but take everything that overlaps rather than assuming 1:1.
            var audio = _audio.Window(int.MaxValue)
                .Where(f => f.StartMs < endMs && f.StartMs + f.DurationMs > startMs)
                .OrderBy(f => f.Sequence)
                .ToList();

            if (audio.Count == 0)
            {
                if (!await _audio.WaitForAsync(sequence, FragmentWaitTimeout, _cts.Token))
                {
                    _log.Warning("SABR LIVE {VideoId}: no audio covering segment {Seq}", _videoId, sequence);
                    return;
                }
                if (_audio.TryGet(sequence, out var single))
                    audio = [single];
            }

            var videoInit = await File.ReadAllBytesAsync(RawPath(true, null));
            var audioInit = await File.ReadAllBytesAsync(RawPath(false, null));
            var videoFragment = await File.ReadAllBytesAsync(RawPath(true, sequence));

            var audioFragments = new List<byte[]>();
            foreach (var f in audio)
            {
                var audioPath = RawPath(false, f.Sequence);
                if (File.Exists(audioPath))
                    audioFragments.Add(await File.ReadAllBytesAsync(audioPath));
            }

            if (audioFragments.Count == 0)
                return;

            await _muxer.MuxAsync(videoInit, videoFragment, audioInit, audioFragments, startMs, endMs,
                (int)(sequence % int.MaxValue), path, Path.Combine(_dir, HlsPlaylist.InitName));
        }
        catch (OperationCanceledException)
        {
            // torn down
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "SABR LIVE {VideoId}: failed to build segment {Seq}", _videoId, sequence);
        }
        finally
        {
            _buildGate.Release();
        }
    }

    // endregion

    private static bool TryParseSegmentName(string fileName, out long sequence)
    {
        sequence = 0;
        if (!fileName.StartsWith("seg_", StringComparison.Ordinal) ||
            !fileName.EndsWith(".m4s", StringComparison.Ordinal))
            return false;
        return long.TryParse(fileName[4..^4], out sequence);
    }

    private string RawPath(bool isVideo, long? sequence) =>
        Path.Combine(_dir, isVideo ? RawVideoDir : RawAudioDir,
            sequence is null ? "init.bin" : $"{sequence}.bin");

    private static async Task WriteAtomicAsync(string path, byte[] data)
    {
        var temp = path + ".part";
        await File.WriteAllBytesAsync(temp, data);
        try { File.Move(temp, path, overwrite: true); }
        catch (IOException) { try { File.Delete(temp); } catch { /* raced */ } }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, true); } catch { /* best effort */ }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { /* already gone */ }
        _cts.Dispose();
        _buildGate.Dispose();
        TryDelete(_dir);
    }
}
