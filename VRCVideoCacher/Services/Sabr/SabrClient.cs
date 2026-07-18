using System.Net.Http.Headers;
using Serilog;

namespace VRCVideoCacher.Services.Sabr;

/// <summary>Everything needed to speak SABR for one video, as extracted by yt-dlp.</summary>
internal sealed class SabrSource
{
    public required string VideoId;
    public required string AbrStreamingUrl;
    /// <summary>Opaque base64url blob from the player response. We never parse it — just pass it back.</summary>
    public required string UstreamerConfig;
    public required ClientInfo ClientInfo;
    public required FormatId AudioFormat;
    public required FormatId VideoFormat;
    /// <summary>True if the selected video format is HDR. Must be reflected in MediaCapabilities.</summary>
    public bool Hdr;

    /// <summary>
    /// Whether to send MediaCapabilities. Required for ANDROID_VR/ANDROID/IOS/VISIONOS; for web clients
    /// it must be omitted, or the server ignores preferred_*_format_ids and serves what it likes.
    /// </summary>
    public bool SendMediaCapabilities = true;

    // Descriptive only — for the HLS master playlist.
    public string VideoCodec = "avc1.4d401f";
    public string AudioCodec = "mp4a.40.2";
    public int Width;
    public int Height;
    public long Bandwidth;
    /// <summary>
    /// The web client's GVS PO token (base64url), minted by the bgutil provider during extraction. The
    /// SABR server attests against it together with the client_info, so it must accompany every request.
    /// </summary>
    public string? PoToken;

    /// <summary>
    /// A live broadcast (yt-dlp <c>live_status == "is_live"</c>). Changes the fetch fundamentally: there
    /// is no end, no <c>sidx</c>, no <c>total_segments</c>, and sequence numbers start in the millions.
    /// <c>post_live</c> is NOT live — it is a finished broadcast being turned into a VOD.
    /// </summary>
    public bool IsLive;

    /// <summary>
    /// The broadcast's target segment duration (<c>_sabr_config.target_duration_sec</c>). Live media
    /// headers frequently omit the duration, so this is what we fall back to. Observed as 2 on a real
    /// stream, so do NOT assume yt-dlp's 5s default.
    /// </summary>
    public int TargetDurationSec;
}

internal sealed record SabrSegment(bool IsVideo, long SequenceNumber, long StartMs, long DurationMs, bool IsInit);

/// <summary>
/// A first-party SABR client. YouTube's SABR delivery is a POST loop: send a protobuf
/// <see cref="VideoPlaybackAbrRequest"/> describing where the player is and what it already has, and
/// the server replies with a UMP stream carrying a forward-contiguous run of fMP4 fragments.
///
/// We own this rather than shelling out to yt-dlp because it is the only way to get <b>seek</b>:
/// <see cref="ClientAbrState.PlayerTimeMs"/> starts playback at an arbitrary time, and a warm client
/// services that in one round-trip instead of the seconds a yt-dlp respawn costs.
///
/// Handles VOD and live broadcasts. Captions and format-switching are deliberately not implemented.
///
/// Live differs in ways that are not configuration but control flow, all driven by
/// <see cref="SabrSource.IsLive"/>: the fetch never completes, an empty response at the broadcast head
/// is normal (wait, don't fail), there is no segment index, segment durations must be estimated, and
/// the entry point is the "live edge" trick — set player_time to <see cref="LiveEdgeStartMs"/> and let
/// the server clamp it. See <see cref="LiveMetadata"/>.
/// </summary>
/// <param name="reloadAsync">
/// Re-runs extraction to get a fresh player response. The server can demand this mid-stream
/// (RELOAD_PLAYER_RESPONSE), and the streaming URL also expires on its own after a few hours.
/// </param>
internal sealed class SabrClient(HttpClient http, SabrSource source, ILogger log,
    Func<CancellationToken, Task<SabrSource>>? reloadAsync = null)
{
    // The server decides how much to send per request; if it sends nothing at all this many times in
    // a row, the stream is stuck and we should fail loudly rather than spin.
    private const int MaxEmptyResponses = 3;
    private const int MaxTransportRetries = 10;
    private const int MaxReloads = 3;
    // A VOD ad makes the server withhold content for a few backoff cycles; allow enough waits to sit
    // through one before we call it a stall.
    private const int MaxAdWaits = 12;

    /// <summary>
    /// How you ask for the live edge. There is no "give me the head" field — the client sends an absurd
    /// player time and the server clamps it to the head (and/or we roll it back ourselves once
    /// <see cref="LiveMetadata"/> tells us where the head is). Value matches yt-dlp's JS_MAX_SAFE_INTEGER.
    /// </summary>
    public const long LiveEdgeStartMs = (1L << 53) - 1;

    // Live: how long we sit at the head getting nothing before concluding the broadcast has ended
    // rather than that we are merely waiting for the next segment.
    private const int LiveEndEmptyResponses = 5;
    private static readonly TimeSpan LiveEndQuietPeriod = TimeSpan.FromSeconds(30);
    // Underestimating a live segment's duration is deliberate: it keeps player_time slightly behind the
    // server's notion of where we are, so we never ask for a segment that does not exist yet.
    private const int LiveDurationToleranceMs = 100;
    private const int LiveDefaultTargetDurationSec = 5;

    private readonly TrackState _audio = new(source.AudioFormat, isVideo: false);
    private readonly TrackState _video = new(source.VideoFormat, isVideo: true);

    // Everything the server can invalidate mid-stream. The formats and all per-track progress survive
    // a reload — only the session's addressing changes — so a reload costs one extraction, not a restart.
    private string _url = source.AbrStreamingUrl;
    private string _ustreamerConfig = source.UstreamerConfig;
    private ClientInfo _clientInfo = source.ClientInfo;
    private string? _poToken = source.PoToken;

    private byte[]? _playbackCookie;
    private long _playerTimeMs;
    private int _reloads;

    // Server-pushed contexts (VOD ads). We must echo the ones marked for sending back on each request,
    // and honour the backoff the server sets, or it never serves the real content.
    private readonly Dictionary<int, SabrContextUpdate> _sabrContextUpdates = new();
    private readonly HashSet<int> _sabrContextsToSend = [];
    private int _pendingBackoffMs;

    // Live broadcast state, from LiveMetadata (part 31). Null until the first one arrives.
    private readonly bool _isLive = source.IsLive;
    private long _liveHeadSequence;
    private long? _minSeekableMs;
    private long? _maxSeekableMs;

    /// <summary>Estimated live segment duration — the fallback when a media header omits it.</summary>
    private int EstSegmentDurationMs =>
        (source.TargetDurationSec > 0 ? source.TargetDurationSec : LiveDefaultTargetDurationSec) * 1000
        - LiveDurationToleranceMs;

    /// <summary>Total duration, known after the first response. 0 until then. Meaningless when live.</summary>
    public long DurationMs { get; private set; }

    /// <summary>The broadcast head's sequence number, or 0 if not live / not yet known.</summary>
    public long LiveHeadSequence => _liveHeadSequence;

    /// <summary>Raised when the broadcast ends: the fetch went quiet while sitting at the head.</summary>
    public bool BroadcastEnded { get; private set; }

    private readonly TaskCompletionSource<SegmentIndex> _segmentIndex =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<SegmentIndex> _audioIndex =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// The exact per-fragment timeline of the video track, available as soon as the head of that track
    /// arrives (one round-trip, a few KB). Awaiting this is what lets a caller publish a complete VOD
    /// playlist immediately rather than waiting for the whole video to be fetched.
    /// </summary>
    public Task<SegmentIndex> SegmentIndexAsync => _segmentIndex.Task;

    /// <summary>The same, for the audio track — which has its own, different fragment boundaries.</summary>
    public Task<SegmentIndex> AudioSegmentIndexAsync => _audioIndex.Task;

    /// <summary>
    /// Receives every fragment as YouTube delivered it, byte for byte. Set this to serve the source
    /// fragments directly instead of piping them through ffmpeg — the segment boundaries are then
    /// correct by construction rather than something we have to predict.
    /// </summary>
    public Func<SabrSegment, byte[], Task>? OnFragment { get; set; }

    /// <summary>
    /// Fetches the video from <paramref name="startTimeMs"/> to the end, writing each track's fMP4
    /// fragments — init segment first, then media segments in sequence order — to its stream. The
    /// result is a valid streamable fMP4 per track, which is exactly what ffmpeg wants.
    /// </summary>
    public async Task DownloadAsync(Stream audioOut, Stream videoOut, long startTimeMs,
        Action<SabrSegment>? onSegment = null, CancellationToken ct = default)
    {
        try
        {
            await DownloadCoreAsync(audioOut, videoOut, startTimeMs, onSegment, ct);
        }
        catch (Exception ex)
        {
            // Never leave a caller awaiting an index on a fetch that has already died.
            //
            // Live has no index and so nobody ever awaits these. Faulting a Task that is never observed
            // means the finalizer thread rethrows it later as an UnobservedTaskException — which surfaces
            // as a pair of errors (one per track) at teardown, long after the cancellation that caused
            // them, and with ErrorPopups on becomes a popup for what is a perfectly normal shutdown.
            if (_isLive)
            {
                _segmentIndex.TrySetCanceled();
                _audioIndex.TrySetCanceled();
            }
            else
            {
                _segmentIndex.TrySetException(ex);
                _audioIndex.TrySetException(ex);
                // Faulted-but-unawaited is possible on VOD too (a fill cancelled by a seek after the
                // indexes were already published), so mark them observed either way.
                Observe(_segmentIndex.Task);
                Observe(_audioIndex.Task);
            }
            throw;
        }

        static void Observe(Task task) => _ = task.ContinueWith(
            t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    private async Task DownloadCoreAsync(Stream audioOut, Stream videoOut, long startTimeMs,
        Action<SabrSegment>? onSegment, CancellationToken ct)
    {
        _audio.Output = audioOut;
        _video.Output = videoOut;
        _playerTimeMs = startTimeMs;
        _audio.OnSegment = _video.OnSegment = onSegment;
        _video.OnIndexCandidate = MakeIndexHandler(_video, _segmentIndex, "video");
        _audio.OnIndexCandidate = MakeIndexHandler(_audio, _audioIndex, "audio");
        _audio.OnFragment = _video.OnFragment = OnFragment;

        var emptyResponses = 0;
        var transportRetries = 0;
        var adWaits = 0;
        var lastActivity = DateTime.UtcNow;

        while (!IsComplete())
        {
            ct.ThrowIfCancellationRequested();

            bool gotSegments;
            try
            {
                gotSegments = await RequestAsync(ct);
                transportRetries = 0;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException && transportRetries < MaxTransportRetries)
            {
                // A retry re-sends an identical request — the protocol is idempotent at request level,
                // because our buffered_ranges already tell the server exactly what we have.
                transportRetries++;
                log.Warning("SABR request failed ({Attempt}/{Max}), retrying: {Error}",
                    transportRetries, MaxTransportRetries, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(transportRetries, 5)), ct);
                continue;
            }

            if (gotSegments)
            {
                emptyResponses = 0;
                adWaits = 0;
                lastActivity = DateTime.UtcNow;
            }
            else if (_isLive && !HasSendableAdContext())
            {
                // An empty response at the broadcast head is the NORMAL state of a live stream that has
                // caught up — the next segment simply does not exist yet. Waiting is correct; the VOD
                // stall guard below would kill the session within three requests.
                emptyResponses++;
                var quietFor = DateTime.UtcNow - lastActivity;
                if (emptyResponses >= LiveEndEmptyResponses && quietFor >= LiveEndQuietPeriod && IsAtLiveHead())
                {
                    log.Information("SABR: broadcast {VideoId} ended (no new media for {Seconds:0}s at the head)",
                        source.VideoId, quietFor.TotalSeconds);
                    BroadcastEnded = true;
                    break;
                }

                var wait = TimeSpan.FromMilliseconds(Math.Max(_pendingBackoffMs, EstSegmentDurationMs));
                log.Verbose("SABR: at live head, waiting {Seconds:0.0}s for the next segment",
                    wait.TotalSeconds);
                await Task.Delay(wait, ct);
            }
            else if (_pendingBackoffMs > 0 && HasSendableAdContext())
            {
                // The server is making the player "watch" a VOD ad before it hands over content. Honour
                // the backoff and re-request (BuildRequest now echoes the ad context) instead of hammering
                // — hammering just trips the stall guard, which is exactly what happened before.
                if (++adWaits > MaxAdWaits)
                    throw new SabrException(
                        $"SABR stream stuck behind an ad after {MaxAdWaits} server-required waits");
                var wait = TimeSpan.FromMilliseconds(_pendingBackoffMs);
                log.Information("SABR: waiting {Seconds:0.0}s for a server-required ad backoff ({Wait}/{Max})",
                    wait.TotalSeconds, adWaits, MaxAdWaits);
                await Task.Delay(wait, ct);
            }
            else if (++emptyResponses >= MaxEmptyResponses)
            {
                throw new SabrException(
                    $"SABR stream stalled: {MaxEmptyResponses} consecutive responses carried no new media " +
                    $"(player time {_playerTimeMs}ms of {DurationMs}ms)");
            }

            AdvancePlayerTime();

            log.Debug("SABR t={PlayerTime}ms | audio seq {ASeq}/{ATotal} ({ABytes:0.0}MiB) | video seq {VSeq}/{VTotal} ({VBytes:0.0}MiB)",
                _playerTimeMs, _audio.LastSequence, _audio.TotalSegments, _audio.BytesWritten / 1048576.0,
                _video.LastSequence, _video.TotalSegments, _video.BytesWritten / 1048576.0);

            // The only safe place to apply backpressure. A response's parts for BOTH tracks must be
            // fully consumed before we pause, or the consumer starves on the track we stopped reading.
            await _audio.Output.FlushAsync(ct);
            await _video.Output.FlushAsync(ct);
        }

        await _audio.Output.FlushAsync(ct);
        await _video.Output.FlushAsync(ct);
        log.Information("SABR fetch complete for {VideoId}: audio {AudioSegs} segments, video {VideoSegs} segments",
            source.VideoId, _audio.LastSequence, _video.LastSequence);
    }

    /// <summary>
    /// The index (sidx for fMP4, Cues for WebM) rides at the head of a track — either in the init
    /// segment or at the front of the first media fragment — so offer it both before giving up.
    /// </summary>
    private Action<byte[], bool> MakeIndexHandler(TrackState track, TaskCompletionSource<SegmentIndex> target,
        string name) =>
        (data, isLastChance) =>
        {
            var total = track.EndTimeMs > 0 ? track.EndTimeMs : DurationMs;
            if (SegmentIndex.TryParse(data, total) is { } index)
            {
                log.Information("{Track} index: {Count} fragments, {Duration:0.0}s",
                    name, index.Count, index.TotalDurationMs / 1000.0);
                target.TrySetResult(index);
            }
            else if (isLastChance && !_isLive)
            {
                target.TrySetException(new SabrException(
                    $"No segment index (sidx/Cues) at the head of the {name} track, so its exact timeline " +
                    "is unavailable and the playlist cannot be published up front"));
            }
            // Live has no index by design — measured: no sidx anywhere in a live fMP4 track. The timeline
            // is discovered fragment by fragment instead, so never fail the fetch over a missing one.
        };

    /// <summary>One POST. Returns whether any new media arrived.</summary>
    private async Task<bool> RequestAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _url);
        request.Content = new ByteArrayContent(BuildRequest());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        request.Headers.Accept.ParseAdd("application/vnd.yt-ump");
        // Identity encoding: the UMP framing is already the transport, and gzip only gets in the way.
        request.Headers.AcceptEncoding.ParseAdd("identity");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(ct);

        var partials = new Dictionary<uint, PartialSegment>();
        var newSegments = false;
        string? redirect = null;
        var reload = false;
        // Reflects only THIS response's NextRequestPolicy; the ad-wait decision reads it after the loop.
        _pendingBackoffMs = 0;

        await foreach (var part in UmpReader.ReadPartsAsync(body, ct))
        {
            switch ((UmpPartId)part.PartId)
            {
                case UmpPartId.FormatInitializationMetadata:
                    InitializeFormat(FormatInitializationMetadata.Decode(part.Payload));
                    break;

                case UmpPartId.MediaHeader:
                {
                    var header = MediaHeader.Decode(part.Payload);
                    if (header.Compressed)
                        throw new SabrException("Server sent a compressed media segment, which we cannot decode");
                    // Live headers routinely omit the duration. Estimating it is not optional: player time
                    // advances by consumed duration, so a zero would leave the fetch stuck in place.
                    if (_isLive && !header.DurationKnown && !header.IsInitSegment)
                        header.DurationMs = EstSegmentDurationMs;
                    var track = TrackFor(header.FormatId);
                    log.Verbose("SABR header: {Track} seq={Seq} init={Init} start={Start} dur={Dur} len={Len} fmt={Fmt}",
                        track?.IsVideo switch { true => "video", false => "audio", null => "UNMATCHED" },
                        header.SequenceNumber, header.IsInitSegment, header.StartMs, header.DurationMs,
                        header.ContentLength, header.FormatId);
                    if (track != null)
                        partials[header.HeaderId] = new PartialSegment(track, header);
                    break;
                }

                case UmpPartId.Media:
                {
                    var (headerId, data) = UmpReader.SplitMediaPayload(part.Payload);
                    if (partials.TryGetValue(headerId, out var partial))
                        partial.Append(data);
                    break;
                }

                case UmpPartId.MediaEnd:
                {
                    var (headerId, _) = UmpReader.SplitMediaPayload(part.Payload);
                    if (partials.Remove(headerId, out var partial) && await partial.CommitAsync(ct))
                        newSegments = true;
                    break;
                }

                case UmpPartId.NextRequestPolicy:
                {
                    // playback_cookie: opaque session token echoed on every later request.
                    // backoff_time_ms: how long the server wants us to wait — a VOD ad "playing" out.
                    var (cookie, backoffMs) = SabrResponse.DecodeNextRequestPolicy(part.Payload);
                    _playbackCookie = cookie ?? _playbackCookie;
                    if (backoffMs > 0)
                        _pendingBackoffMs = backoffMs;
                    break;
                }

                case UmpPartId.LiveMetadata:
                    ProcessLiveMetadata(LiveMetadata.Decode(part.Payload));
                    break;

                case UmpPartId.SabrContextUpdate:
                    ProcessSabrContextUpdate(SabrContextUpdate.Decode(part.Payload));
                    break;

                case UmpPartId.SabrContextSendingPolicy:
                    ProcessSabrContextSendingPolicy(SabrContextSendingPolicy.Decode(part.Payload));
                    break;

                case UmpPartId.SabrRedirect:
                    redirect = SabrResponse.DecodeRedirectUrl(part.Payload);
                    break;

                case UmpPartId.StreamProtectionStatus:
                    // 3 = ATTESTATION_REQUIRED. We send the web client's PO token on every request, so this
                    // means the token the bgutil provider minted was rejected or has expired — not that one
                    // was missing.
                    if (SabrResponse.DecodeProtectionStatus(part.Payload) == 3)
                        throw new SabrException(
                            "YouTube rejected our PO token (attestation required). The bgutil-minted token " +
                            "may be invalid or expired.");
                    break;

                case UmpPartId.SabrError:
                    throw new SabrException($"Server returned SabrError: {SabrResponse.DecodeError(part.Payload)}");

                case UmpPartId.ReloadPlayerResponse:
                    // The server has invalidated our session and wants a fresh player response. Our
                    // segment progress stays valid, so this costs one extraction rather than a restart.
                    reload = true;
                    break;

                case UmpPartId.SabrSeek:
                {
                    // Server-initiated reposition. On a VOD this should never happen and means we have
                    // misunderstood the stream; on live it is routine (the DVR window slid past us).
                    var seek = SabrSeek.Decode(part.Payload);
                    if (!_isLive)
                        throw new SabrException("Server sent SabrSeek on a VOD stream, which we do not handle");
                    if (seek.SeekToMs is not { } seekTo)
                        throw new SabrException("Server sent a SabrSeek with no usable seek time");
                    log.Information("SABR: server seek to {SeekTo}ms (was {Was}ms)", seekTo, _playerTimeMs);
                    ApplyServerSeek(seekTo);
                    break;
                }

                default:
                    log.Verbose("Ignoring UMP part {PartId} ({Size} bytes)", part.PartId, part.Payload.Length);
                    break;
            }
        }

        if (redirect != null)
        {
            log.Debug("Following SABR redirect");
            _url = redirect;
        }

        if (reload)
            await ReloadAsync(ct);

        return newSegments;
    }

    /// <summary>
    /// Swaps in a fresh player response. Deliberately keeps the formats and every track's buffered
    /// range: the server only invalidated how we address the session, not what we already hold — so
    /// the next request picks up exactly where we left off.
    /// </summary>
    private async Task ReloadAsync(CancellationToken ct)
    {
        if (reloadAsync is null)
            throw new SabrException("Server demanded a player-response reload, but no reload callback was supplied");
        if (++_reloads > MaxReloads)
            throw new SabrException($"Server demanded more than {MaxReloads} player-response reloads; giving up");

        log.Information("Reloading SABR player response ({Reload}/{Max}) at {PlayerTime}ms",
            _reloads, MaxReloads, _playerTimeMs);

        var refreshed = await reloadAsync(ct);
        _url = refreshed.AbrStreamingUrl;
        _ustreamerConfig = refreshed.UstreamerConfig;
        _clientInfo = refreshed.ClientInfo;
        _poToken = refreshed.PoToken;
        // The cookie belongs to the old session; the new player response will issue another.
        _playbackCookie = null;
        // Ad/session context ids are scoped to the old session — drop them (matches yt-dlp on reload).
        _sabrContextUpdates.Clear();
        _sabrContextsToSend.Clear();
        _pendingBackoffMs = 0;
    }

    /// <summary>
    /// Registers a server-pushed context (typically a VOD ad). Mirrors yt-dlp: invalid updates and
    /// KEEP_EXISTING duplicates are ignored; send_by_default marks it for immediate echoing.
    /// </summary>
    private void ProcessSabrContextUpdate(SabrContextUpdate update)
    {
        if (!update.IsValid)
        {
            log.Warning("Ignoring an invalid SabrContextUpdate");
            return;
        }
        const int keepExisting = 2;
        if (update.WritePolicy == keepExisting && _sabrContextUpdates.ContainsKey(update.Type))
            return;

        _sabrContextUpdates[update.Type] = update;
        if (update.SendByDefault)
            _sabrContextsToSend.Add(update.Type);
        log.Debug("Registered SabrContextUpdate type {Type} ({Bytes} bytes){Send}",
            update.Type, update.Value!.Length, update.SendByDefault ? ", sending" : "");
    }

    private void ProcessSabrContextSendingPolicy(SabrContextSendingPolicy policy)
    {
        foreach (var type in policy.Start)
            _sabrContextsToSend.Add(type);
        foreach (var type in policy.Stop)
            _sabrContextsToSend.Remove(type);
        foreach (var type in policy.Discard)
            _sabrContextUpdates.Remove(type);
    }

    /// <summary>True while the server is holding content behind an ad we're being asked to "play".</summary>
    private bool HasSendableAdContext() => _sabrContextsToSend.Any(_sabrContextUpdates.ContainsKey);

    private byte[] BuildRequest()
    {
        var request = new VideoPlaybackAbrRequest
        {
            ClientAbrState = new ClientAbrState
            {
                PlayerTimeMs = _playerTimeMs,
                // Required for ANDROID_VR; sending it to a web client would make the server ignore
                // our preferred formats entirely. The HDR flag must match the itag we're asking for.
                MediaCapabilities = source.SendMediaCapabilities ? new MediaCapabilities(source.Hdr) : null,
            },
            VideoPlaybackUstreamerConfig = Base64Url(_ustreamerConfig),
            PreferredAudioFormatIds = [source.AudioFormat],
            PreferredVideoFormatIds = [source.VideoFormat],
            StreamerContext = new StreamerContext
            {
                ClientInfo = _clientInfo,
                PoToken = _poToken is null ? null : Base64Url(_poToken),
                PlaybackCookie = _playbackCookie,
                // Echo back the ad/session contexts the server is waiting on, plus any it wants that we
                // haven't received the value for yet.
                SabrContexts = _sabrContextsToSend
                    .Where(_sabrContextUpdates.ContainsKey)
                    .Select(type => new SabrContext(type, _sabrContextUpdates[type].Value!))
                    .ToList(),
                UnsentSabrContexts = _sabrContextsToSend
                    .Where(type => !_sabrContextUpdates.ContainsKey(type))
                    .ToList(),
            },
        };

        foreach (var track in Tracks)
        {
            // Only claim a format is initialized once we actually hold its init segment. Claiming it
            // early means a retry during the init segment never gets it re-sent.
            if (track.HasInitSegment)
                request.InitializedFormatIds.Add(track.FormatId);
            if (track.Consumed != null)
                request.BufferedRanges.Add(track.Consumed);
        }

        return Encode(request);
    }

    private static byte[] Encode(IProtoMessage message)
    {
        var writer = new ProtoWriter();
        message.WriteTo(writer);
        return writer.ToArray();
    }

    private void InitializeFormat(FormatInitializationMetadata metadata)
    {
        var track = TrackFor(metadata.FormatId);
        if (track == null)
            return;

        // VOD declares its size once and it never changes, so latching the first value is right. A live
        // broadcast's "total" is the moving head, re-sent as it grows — latching would freeze it.
        if (track.TotalSegments > 0 && !_isLive)
            return;

        track.TotalSegments = metadata.TotalSegments;
        track.EndTimeMs = metadata.EndTimeMs;
        DurationMs = Math.Max(DurationMs, metadata.DurationMs);

        log.Debug("SABR format {FormatId} initialized: {Segments} segments, {Duration}ms, {Mime}",
            metadata.FormatId, metadata.TotalSegments, metadata.DurationMs, metadata.MimeType);
    }

    /// <summary>
    /// Where the broadcast is now. This is the only authoritative head signal on live — there is no
    /// growing index and no total_segments — so it drives both "have we caught up" and the DVR bounds.
    /// </summary>
    private void ProcessLiveMetadata(LiveMetadata metadata)
    {
        if (metadata.HeadSequenceNumber > 0)
        {
            _liveHeadSequence = metadata.HeadSequenceNumber;
            // Live never sends total_segments; the head is the closest thing to it.
            foreach (var track in Tracks)
                track.TotalSegments = metadata.HeadSequenceNumber;
        }

        _minSeekableMs = metadata.MinSeekableTimeMs ?? _minSeekableMs;
        _maxSeekableMs = metadata.MaxSeekableTimeMs ?? _maxSeekableMs;

        // The DVR window slid past us — we are asking for segments the server no longer keeps, and it
        // will simply serve nothing forever. The server SHOULD send SabrSeek here but does not always,
        // so jump forward ourselves.
        if (_minSeekableMs is { } min && _playerTimeMs < min)
        {
            log.Information("SABR: player time {Player}ms fell behind the DVR window ({Min}ms); jumping forward",
                _playerTimeMs, min);
            ApplyServerSeek(min);
        }
    }

    /// <summary>
    /// Repositions the fetch. The consumed ranges describe media we no longer hold contiguously with the
    /// new position, so they must be dropped or the server would think we already have what follows.
    /// </summary>
    private void ApplyServerSeek(long toMs)
    {
        _playerTimeMs = toMs;
        foreach (var track in Tracks)
            track.Consumed = null;
    }

    /// <summary>Have we caught up with the broadcast? Used only to tell "ended" from "still producing".</summary>
    private bool IsAtLiveHead()
    {
        // No LiveMetadata at all means we cannot know — treat as at-head so a dead stream can still end
        // rather than hanging forever.
        if (_liveHeadSequence <= 0 && _maxSeekableMs is null)
            return true;

        if (_maxSeekableMs is { } max && _playerTimeMs + EstSegmentDurationMs >= max)
            return true;

        return _liveHeadSequence > 0 &&
               Tracks.All(t => t.LastSequence > 0 && _liveHeadSequence - t.LastSequence <= LiveHeadSegmentTolerance);
    }

    // The last segment or two of a broadcast is often never served, so "at the head" has to be fuzzy.
    private const int LiveHeadSegmentTolerance = 4;

    /// <summary>
    /// Player time advances to the end of the slowest track's consumed range — that is the point up to
    /// which we hold BOTH audio and video, and therefore what the server should continue from.
    /// </summary>
    private void AdvancePlayerTime()
    {
        var next = Tracks.Min(t => t.ConsumedEndMs);
        if (next > _playerTimeMs)
            _playerTimeMs = next;

        // Live starts at LiveEdgeStartMs, an absurd time the server clamps by serving us the head. Until
        // we know where the head is we must not keep asking from the far future, or nothing ever arrives.
        if (_isLive && _maxSeekableMs is { } max && _playerTimeMs > max + EstSegmentDurationMs)
        {
            var floor = Tracks.Max(t => t.ConsumedEndMs);
            _playerTimeMs = Math.Max(floor, max + EstSegmentDurationMs);
        }
    }

    /// <summary>A live broadcast is never complete; it ends by going quiet at the head.</summary>
    private bool IsComplete() =>
        !_isLive &&
        Tracks.All(t => t.TotalSegments > 0) &&
        (Tracks.All(t => t.LastSequence >= t.TotalSegments) ||
         Tracks.All(t => t.EndTimeMs > 0 && _playerTimeMs >= t.EndTimeMs));

    private TrackState[] Tracks => [_audio, _video];

    private TrackState? TrackFor(FormatId? formatId) =>
        _audio.FormatId.Matches(formatId) ? _audio :
        _video.FormatId.Matches(formatId) ? _video : null;

    private static byte[] Base64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
    }

    private sealed class TrackState(FormatId formatId, bool isVideo)
    {
        public readonly FormatId FormatId = formatId;
        public readonly bool IsVideo = isVideo;

        public Stream Output = Stream.Null;
        public Action<SabrSegment>? OnSegment;
        public Func<SabrSegment, byte[], Task>? OnFragment;
        /// <summary>(bytes, isLastChance) — offered the head of the track until the index turns up.</summary>
        public Action<byte[], bool>? OnIndexCandidate;

        public long TotalSegments;
        public long EndTimeMs;
        public bool HasInitSegment;
        public long LastSequence;
        public long BytesWritten;

        /// <summary>What we hold, as the server needs to see it. One contiguous range: we never skip.</summary>
        public BufferedRange? Consumed;

        public long ConsumedEndMs => Consumed is null ? 0 : Consumed.StartTimeMs + Consumed.DurationMs;

        public void Record(MediaHeader header)
        {
            if (header.IsInitSegment)
            {
                HasInitSegment = true;
                return;
            }

            LastSequence = header.SequenceNumber;
            if (Consumed is null)
            {
                Consumed = new BufferedRange
                {
                    FormatId = FormatId,
                    StartTimeMs = header.StartMs,
                    DurationMs = header.DurationMs,
                    StartSegmentIndex = header.SequenceNumber,
                    EndSegmentIndex = header.SequenceNumber,
                };
                return;
            }

            Consumed.EndSegmentIndex = header.SequenceNumber;
            Consumed.DurationMs = header.StartMs - Consumed.StartTimeMs + header.DurationMs;
        }

        /// <summary>True if this segment is one we already have (the server re-sends after a retry).</summary>
        public bool AlreadyHave(MediaHeader header) =>
            header.IsInitSegment ? HasInitSegment : Consumed != null && header.SequenceNumber <= Consumed.EndSegmentIndex;
    }

    private sealed class PartialSegment(TrackState track, MediaHeader header)
    {
        private readonly MemoryStream _data = new();

        public void Append(ReadOnlyMemory<byte> chunk) => _data.Write(chunk.Span);

        /// <summary>Writes the completed fragment to the track's stream. False if it was a duplicate.</summary>
        public async Task<bool> CommitAsync(CancellationToken ct)
        {
            if (header.ContentLength is { } expected && _data.Length != expected)
                throw new SabrException(
                    $"Segment {header.SequenceNumber} truncated: expected {expected} bytes, got {_data.Length}");

            if (track.AlreadyHave(header))
                return false;

            // The index rides at the head of the track: NOT inside the init segment (ftyp+moov) but
            // immediately after it, at the front of the first media fragment. Offer both before giving
            // up, and do it before the bytes reach ffmpeg, which strips the index when it re-fragments.
            if (header.IsInitSegment || header.SequenceNumber <= 1)
                track.OnIndexCandidate?.Invoke(_data.ToArray(), !header.IsInitSegment);

            if (track.OnFragment is { } onFragment)
                await onFragment(
                    new SabrSegment(track.IsVideo, header.SequenceNumber, header.StartMs, header.DurationMs,
                        header.IsInitSegment),
                    _data.ToArray());

            _data.Position = 0;
            await _data.CopyToAsync(track.Output, ct);
            track.Record(header);
            track.BytesWritten += _data.Length;
            track.OnSegment?.Invoke(new SabrSegment(
                track.IsVideo, header.SequenceNumber, header.StartMs, header.DurationMs, header.IsInitSegment));
            return true;
        }
    }
}

internal sealed class SabrException(string message) : Exception(message);
