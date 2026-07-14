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
    /// <summary>Null on android_vr, which is why we prefer it — a PO token means a JS/BotGuard provider.</summary>
    public string? PoToken;
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
/// VOD only — live/DVR, ads, captions and format-switching are deliberately not implemented.
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

    /// <summary>Total duration, known after the first response. 0 until then.</summary>
    public long DurationMs { get; private set; }

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
            _segmentIndex.TrySetException(ex);
            _audioIndex.TrySetException(ex);
            throw;
        }
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
            else if (isLastChance)
            {
                target.TrySetException(new SabrException(
                    $"No segment index (sidx/Cues) at the head of the {name} track, so its exact timeline " +
                    "is unavailable and the playlist cannot be published up front"));
            }
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
                    var track = TrackFor(header.FormatId);
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
                    // Opaque session token; the server expects it back on every subsequent request.
                    _playbackCookie = SabrResponse.DecodePlaybackCookie(part.Payload) ?? _playbackCookie;
                    break;

                case UmpPartId.SabrRedirect:
                    redirect = SabrResponse.DecodeRedirectUrl(part.Payload);
                    break;

                case UmpPartId.StreamProtectionStatus:
                    // 3 = ATTESTATION_REQUIRED. We deliberately use a client that needs no PO token, so
                    // this means YouTube has closed that window rather than that we forgot one.
                    if (SabrResponse.DecodeProtectionStatus(part.Payload) == 3)
                        throw new SabrException(
                            "YouTube demanded attestation (a PO token) for this stream. The android_vr " +
                            "no-token path may no longer be available.");
                    break;

                case UmpPartId.SabrError:
                    throw new SabrException($"Server returned SabrError: {SabrResponse.DecodeError(part.Payload)}");

                case UmpPartId.ReloadPlayerResponse:
                    // The server has invalidated our session and wants a fresh player response. Our
                    // segment progress stays valid, so this costs one extraction rather than a restart.
                    reload = true;
                    break;

                case UmpPartId.SabrSeek:
                    // Server-initiated seek. Only meaningful for live/DVR, which we don't serve.
                    throw new SabrException("Server sent SabrSeek, which this VOD-only client does not handle");

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
    }

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
        if (track == null || track.TotalSegments > 0)
            return;

        track.TotalSegments = metadata.TotalSegments;
        track.EndTimeMs = metadata.EndTimeMs;
        DurationMs = Math.Max(DurationMs, metadata.DurationMs);

        log.Debug("SABR format {FormatId} initialized: {Segments} segments, {Duration}ms, {Mime}",
            metadata.FormatId, metadata.TotalSegments, metadata.DurationMs, metadata.MimeType);
    }

    /// <summary>
    /// Player time advances to the end of the slowest track's consumed range — that is the point up to
    /// which we hold BOTH audio and video, and therefore what the server should continue from.
    /// </summary>
    private void AdvancePlayerTime()
    {
        var next = Tracks.Min(t => t.ConsumedEndMs);
        if (next > _playerTimeMs)
            _playerTimeMs = next;
    }

    private bool IsComplete() =>
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
