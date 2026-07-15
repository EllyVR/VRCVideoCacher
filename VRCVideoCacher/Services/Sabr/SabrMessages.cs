namespace VRCVideoCacher.Services.Sabr;

// The SABR wire schema, reverse-engineered by the yt-dlp SABR work. Field numbers are load-bearing
// and are NOT sequential — they are transcribed from
// yt-dlp/yt_dlp/extractor/youtube/_proto/videostreaming/*. Do not "tidy" them.
//
// Only what a read-only VOD fetch needs is modelled: live/DVR, ads, captions and onesie are omitted.

// region: request

internal sealed class FormatId : IProtoMessage
{
    public int Itag;
    public ulong? Lmt;
    public string? Xtags;

    public void WriteTo(ProtoWriter w)
    {
        w.Varint(1, Itag);
        w.Varint(2, Lmt);
        w.String(3, Xtags);
    }

    public static FormatId Decode(ReadOnlySpan<byte> data)
    {
        var result = new FormatId();
        var r = new ProtoReader(data);
        while (r.Next(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == 0: result.Itag = (int)r.ReadVarint(); break;
                case 2 when wire == 0: result.Lmt = r.ReadVarint(); break;
                case 3 when wire == 2: result.Xtags = r.ReadString(); break;
                default: r.Skip(wire); break;
            }
        }
        return result;
    }

    /// <summary>Identity for matching a served segment back to the format we asked for.</summary>
    public bool Matches(FormatId? other) => other != null && other.Itag == Itag;

    public override string ToString() => $"itag={Itag}";
}

internal sealed class TimeRange(long startTicks, long durationTicks, int timescale) : IProtoMessage
{
    public void WriteTo(ProtoWriter w)
    {
        w.Varint(1, startTicks);
        w.Varint(2, durationTicks);
        w.Varint(3, timescale);
    }
}

internal sealed class BufferedRange : IProtoMessage
{
    public FormatId? FormatId;
    public long StartTimeMs;
    public long DurationMs;
    public long StartSegmentIndex;
    public long EndSegmentIndex;

    public void WriteTo(ProtoWriter w)
    {
        w.Message(1, FormatId);
        w.Varint(2, StartTimeMs);
        w.Varint(3, DurationMs);
        w.Varint(4, StartSegmentIndex);
        w.Varint(5, EndSegmentIndex);
        w.Message(6, new TimeRange(StartTimeMs, DurationMs, 1000));
    }
}

/// <summary>
/// Advertises codec support. Must be sent for ANDROID_VR/ANDROID/IOS/VISIONOS, and must NOT be sent
/// for web clients — the server then ignores preferred_*_format_ids and serves whatever it likes.
///
/// <paramref name="hdr"/> must match the format actually being requested: HDR and SDR cannot both be
/// enabled, and asking for an HDR itag (e.g. vp9.2) while advertising SDR makes the server refuse to
/// serve anything and demand a player-response reload forever.
/// </summary>
internal sealed class MediaCapabilities(bool hdr) : IProtoMessage
{
    // VideoCodec enum values from the schema; advertise them all as efficient + 10-bit capable.
    private static readonly int[] VideoCodecs = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    public void WriteTo(ProtoWriter w)
    {
        foreach (var codec in VideoCodecs)
            w.Message(1, new VideoFormatCapability(codec));
        w.Varint(5, hdr ? 3 : 0); // hdr_mode_bitmask: 3 = HDR, 0 = SDR
    }

    private sealed class VideoFormatCapability(int codec) : IProtoMessage
    {
        public void WriteTo(ProtoWriter w)
        {
            w.Varint(1, codec);
            w.Bool(2, true);   // efficient
            w.Bool(15, true);  // is_10_bit_supported
        }
    }
}

internal sealed class ClientAbrState : IProtoMessage
{
    // Audio+video. SABR has no video-only mode: to drop a track you mark it fully buffered instead.
    public const int BitfieldAudioVideo = 0;

    /// <summary>The seek lever. Seeding this is how you start playback at an arbitrary time.</summary>
    public long PlayerTimeMs;
    public MediaCapabilities? MediaCapabilities;

    public void WriteTo(ProtoWriter w)
    {
        w.Varint(28, PlayerTimeMs);
        w.Message(38, MediaCapabilities);
        w.Varint(40, BitfieldAudioVideo);
        w.Bool(46, true); // drc_enabled — required to stream DRC formats
        w.Bool(76, true); // enable_voice_boost
    }
}

/// <summary>
/// The schema also has hl(1), gl(2), visitor_data(14) and user_agent(15), but yt-dlp populates none
/// of them for SABR, so neither do we — these are exactly the fields it puts on the wire.
/// </summary>
internal sealed class ClientInfo : IProtoMessage
{
    public string? DeviceMake;
    public string? DeviceModel;
    public int? ClientName;
    public string? ClientVersion;
    public string? OsName;
    public string? OsVersion;
    public int? AndroidSdkVersion;

    public void WriteTo(ProtoWriter w)
    {
        w.String(12, DeviceMake);
        w.String(13, DeviceModel);
        w.Varint(16, ClientName);
        w.String(17, ClientVersion);
        w.String(18, OsName);
        w.String(19, OsVersion);
        w.Varint(64, AndroidSdkVersion);
    }
}

/// <summary>One ad/session context the server told us to echo back. Opaque — we never parse the value.</summary>
internal sealed class SabrContext(int type, byte[] value) : IProtoMessage
{
    public void WriteTo(ProtoWriter w)
    {
        w.Varint(1, type);
        w.Bytes(2, value);
    }
}

internal sealed class StreamerContext : IProtoMessage
{
    public ClientInfo? ClientInfo;
    public byte[]? PoToken;
    public byte[]? PlaybackCookie;
    /// <summary>Ad/session contexts the server asked us to send back (VOD ads gate content behind this).</summary>
    public List<SabrContext> SabrContexts = [];
    /// <summary>Context types the server wants but whose value we haven't received yet.</summary>
    public List<int> UnsentSabrContexts = [];

    public void WriteTo(ProtoWriter w)
    {
        w.Message(1, ClientInfo);
        w.Bytes(2, PoToken);
        w.Bytes(3, PlaybackCookie);
        w.Messages(5, SabrContexts);
        // Repeated int32, written unpacked (one tag each) — the server accepts either encoding.
        foreach (var type in UnsentSabrContexts)
            w.Varint(6, type);
    }
}

internal sealed class VideoPlaybackAbrRequest : IProtoMessage
{
    public ClientAbrState? ClientAbrState;
    public List<FormatId> InitializedFormatIds = [];
    public List<BufferedRange> BufferedRanges = [];
    public byte[]? VideoPlaybackUstreamerConfig;
    public List<FormatId> PreferredAudioFormatIds = [];
    public List<FormatId> PreferredVideoFormatIds = [];
    public StreamerContext? StreamerContext;

    public void WriteTo(ProtoWriter w)
    {
        w.Message(1, ClientAbrState);
        w.Messages(2, InitializedFormatIds);
        w.Messages(3, BufferedRanges);
        w.Bytes(5, VideoPlaybackUstreamerConfig);
        w.Messages(16, PreferredAudioFormatIds);
        w.Messages(17, PreferredVideoFormatIds);
        w.Message(19, StreamerContext);
    }
}

// endregion
// region: response

internal sealed class MediaHeader
{
    public uint HeaderId;
    public bool IsInitSegment;
    public long SequenceNumber;
    public long StartMs;
    public long DurationMs;
    public FormatId? FormatId;
    public long? ContentLength;
    public bool Compressed;

    // time_range, used when start_ms/duration_ms are absent.
    private long _rangeStartTicks;
    private long _rangeDurationTicks;
    private int _rangeTimescale;

    public static MediaHeader Decode(ReadOnlySpan<byte> data)
    {
        var result = new MediaHeader();
        var r = new ProtoReader(data);
        while (r.Next(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == 0: result.HeaderId = (uint)r.ReadVarint(); break;
                case 7 when wire == 0: result.Compressed = r.ReadVarint() != 0; break;
                case 8 when wire == 0: result.IsInitSegment = r.ReadVarint() != 0; break;
                case 9 when wire == 0: result.SequenceNumber = (long)r.ReadVarint(); break;
                case 11 when wire == 0: result.StartMs = (long)r.ReadVarint(); break;
                case 12 when wire == 0: result.DurationMs = (long)r.ReadVarint(); break;
                case 13 when wire == 2: result.FormatId = Sabr.FormatId.Decode(r.ReadBytes()); break;
                case 14 when wire == 0: result.ContentLength = (long)r.ReadVarint(); break;
                case 15 when wire == 2: result.ReadTimeRange(r.ReadBytes()); break;
                default: r.Skip(wire); break;
            }
        }
        // VOD headers normally carry start_ms/duration_ms outright; time_range is the fallback encoding.
        if (result is { DurationMs: 0, _rangeTimescale: > 0 })
        {
            result.StartMs = result._rangeStartTicks * 1000 / result._rangeTimescale;
            result.DurationMs = result._rangeDurationTicks * 1000 / result._rangeTimescale;
        }
        return result;
    }

    private void ReadTimeRange(ReadOnlySpan<byte> data)
    {
        var r = new ProtoReader(data);
        while (r.Next(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == 0: _rangeStartTicks = (long)r.ReadVarint(); break;
                case 2 when wire == 0: _rangeDurationTicks = (long)r.ReadVarint(); break;
                case 3 when wire == 0: _rangeTimescale = (int)r.ReadVarint(); break;
                default: r.Skip(wire); break;
            }
        }
    }
}

internal sealed class FormatInitializationMetadata
{
    public FormatId? FormatId;
    public long EndTimeMs;
    public long TotalSegments;
    public string? MimeType;
    public long DurationTicks;
    public int DurationTimescale;

    /// <summary>Total duration in ms, or 0 if the server didn't say.</summary>
    public long DurationMs => DurationTimescale > 0 ? DurationTicks * 1000 / DurationTimescale : EndTimeMs;

    public static FormatInitializationMetadata Decode(ReadOnlySpan<byte> data)
    {
        var result = new FormatInitializationMetadata();
        var r = new ProtoReader(data);
        while (r.Next(out var field, out var wire))
        {
            switch (field)
            {
                case 2 when wire == 2: result.FormatId = Sabr.FormatId.Decode(r.ReadBytes()); break;
                case 3 when wire == 0: result.EndTimeMs = (long)r.ReadVarint(); break;
                case 4 when wire == 0: result.TotalSegments = (long)r.ReadVarint(); break;
                case 5 when wire == 2: result.MimeType = r.ReadString(); break;
                case 9 when wire == 0: result.DurationTicks = (long)r.ReadVarint(); break;
                case 10 when wire == 0: result.DurationTimescale = (int)r.ReadVarint(); break;
                default: r.Skip(wire); break;
            }
        }
        return result;
    }
}

/// <summary>
/// Server-pushed context (part 57), most commonly a VOD ad. The server won't serve real content until
/// we echo this back (as a <see cref="SabrContext"/>) on subsequent requests. Value is opaque.
/// </summary>
internal sealed class SabrContextUpdate
{
    public int Type;
    public byte[]? Value;
    public bool SendByDefault;
    public int WritePolicy; // 1 = OVERWRITE, 2 = KEEP_EXISTING (0 = unspecified ⇒ invalid)

    /// <summary>yt-dlp ignores updates missing any of type/value/write_policy.</summary>
    public bool IsValid => Type != 0 && Value is { Length: > 0 } && WritePolicy != 0;

    public static SabrContextUpdate Decode(ReadOnlySpan<byte> data)
    {
        var result = new SabrContextUpdate();
        var r = new ProtoReader(data);
        while (r.Next(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == 0: result.Type = (int)r.ReadVarint(); break;
                case 3 when wire == 2: result.Value = r.ReadBytes().ToArray(); break;
                case 4 when wire == 0: result.SendByDefault = r.ReadVarint() != 0; break;
                case 5 when wire == 0: result.WritePolicy = (int)r.ReadVarint(); break;
                default: r.Skip(wire); break; // scope(2) and anything else are not needed
            }
        }
        return result;
    }
}

/// <summary>SabrContextSendingPolicy (part 59): the server toggling which context types we echo.</summary>
internal sealed class SabrContextSendingPolicy
{
    public readonly List<int> Start = [];
    public readonly List<int> Stop = [];
    public readonly List<int> Discard = [];

    public static SabrContextSendingPolicy Decode(ReadOnlySpan<byte> data)
    {
        var result = new SabrContextSendingPolicy();
        var r = new ProtoReader(data);
        while (r.Next(out var field, out var wire))
        {
            // Repeated int32 may arrive packed (wire 2) or one-per-tag (wire 0); handle both.
            switch (field)
            {
                case 1: ReadInts(ref r, wire, result.Start); break;
                case 2: ReadInts(ref r, wire, result.Stop); break;
                case 3: ReadInts(ref r, wire, result.Discard); break;
                default: r.Skip(wire); break;
            }
        }
        return result;
    }

    private static void ReadInts(ref ProtoReader r, int wire, List<int> into)
    {
        if (wire == 0)
        {
            into.Add((int)r.ReadVarint());
            return;
        }
        if (wire != 2)
        {
            r.Skip(wire);
            return;
        }

        // Packed: a length-delimited block of consecutive raw varints (NOT tag-prefixed).
        var block = r.ReadBytes();
        var pos = 0;
        while (pos < block.Length)
        {
            ulong value = 0;
            var shift = 0;
            while (pos < block.Length)
            {
                var b = block[pos++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;
                shift += 7;
            }
            into.Add((int)value);
        }
    }
}

internal static class SabrResponse
{
    /// <summary>SabrRedirect.redirect_url — fires on nearly every first request.</summary>
    public static string? DecodeRedirectUrl(ReadOnlySpan<byte> data)
    {
        var r = new ProtoReader(data);
        while (r.Next(out var field, out var wire))
        {
            if (field == 1 && wire == 2)
                return r.ReadString();
            r.Skip(wire);
        }
        return null;
    }

    /// <summary>
    /// NextRequestPolicy: the playback_cookie (opaque, echoed back on every later request) and
    /// backoff_time_ms (how long the server wants us to wait before the next request — used to make a
    /// VOD "play" an ad before it serves content).
    /// </summary>
    public static (byte[]? cookie, int backoffMs) DecodeNextRequestPolicy(ReadOnlySpan<byte> data)
    {
        byte[]? cookie = null;
        var backoff = 0;
        var r = new ProtoReader(data);
        while (r.Next(out var field, out var wire))
        {
            switch (field)
            {
                case 4 when wire == 0: backoff = (int)r.ReadVarint(); break;
                case 7 when wire == 2: cookie = r.ReadBytes().ToArray(); break;
                default: r.Skip(wire); break;
            }
        }
        return (cookie, backoff);
    }

    /// <summary>StreamProtectionStatus.status: 1=OK, 2=ATTESTATION_PENDING, 3=ATTESTATION_REQUIRED.</summary>
    public static int DecodeProtectionStatus(ReadOnlySpan<byte> data)
    {
        var r = new ProtoReader(data);
        while (r.Next(out var field, out var wire))
        {
            if (field == 1 && wire == 0)
                return (int)r.ReadVarint();
            r.Skip(wire);
        }
        return 0;
    }

    /// <summary>SabrError, rendered for logs.</summary>
    public static string DecodeError(ReadOnlySpan<byte> data)
    {
        string? type = null;
        var r = new ProtoReader(data);
        while (r.Next(out var field, out var wire))
        {
            if (field == 1 && wire == 2)
                type = r.ReadString();
            else
                r.Skip(wire);
        }
        return type ?? "unknown";
    }
}

// endregion
