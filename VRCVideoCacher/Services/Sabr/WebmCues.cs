namespace VRCVideoCacher.Services.Sabr;

/// <summary>
/// Reads the segment timeline out of a WebM track's Matroska <c>Cues</c> element.
///
/// YouTube serves H.264 as fMP4 (indexed by a <c>sidx</c> box) but VP9 and AV1 as WebM, which has no
/// sidx. WebM instead carries a <c>Cues</c> element listing every cluster's start time, and YouTube
/// puts it in the init segment ahead of the first cluster — so, exactly like sidx, the whole timeline
/// is available before any media is fetched.
///
/// Only the handful of EBML elements needed for that are parsed; everything else is skipped.
/// </summary>
internal static class WebmCues
{
    private const uint Segment = 0x18538067;
    private const uint Info = 0x1549A966;
    private const uint Cues = 0x1C53BB6B;
    private const uint TimestampScale = 0x2AD7B1;
    private const uint CuePoint = 0xBB;
    private const uint CueTime = 0xB3;

    public static SegmentIndex? TryParse(ReadOnlySpan<byte> data, long totalDurationMs)
    {
        try
        {
            if (!TryFindChild(data, Segment, out var segment))
                return null;

            // Cluster timestamps are in units of TimestampScale nanoseconds (default 1ms).
            var scaleNs = 1_000_000L;
            if (TryFindChild(segment, Info, out var info) && TryFindChild(info, TimestampScale, out var scale))
                scaleNs = (long)ReadUInt(scale);

            if (!TryFindChild(segment, Cues, out var cues))
                return null;

            var startsMs = new List<long>();
            var pos = 0;
            while (TryReadElement(cues, ref pos, out var id, out var body))
            {
                if (id != CuePoint)
                    continue;
                if (TryFindChild(body, CueTime, out var time))
                    startsMs.Add((long)ReadUInt(time) * scaleNs / 1_000_000L);
            }

            if (startsMs.Count == 0)
                return null;

            // Cues give starts; a duration is the gap to the next start. The last one needs the total,
            // which the server already told us in FormatInitializationMetadata.
            var durations = new long[startsMs.Count];
            for (var i = 0; i < startsMs.Count - 1; i++)
                durations[i] = startsMs[i + 1] - startsMs[i];

            var lastStart = startsMs[^1];
            if (totalDurationMs <= lastStart)
                return null; // can't size the final cluster; a wrong playlist is worse than none
            durations[^1] = totalDurationMs - lastStart;

            return new SegmentIndex { DurationsMs = durations };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null; // truncated
        }
    }

    /// <summary>Scans one level for a child element, returning its body.</summary>
    private static bool TryFindChild(ReadOnlySpan<byte> parent, uint wanted, out ReadOnlySpan<byte> body)
    {
        var pos = 0;
        while (TryReadElement(parent, ref pos, out var id, out var candidate))
        {
            if (id == wanted)
            {
                body = candidate;
                return true;
            }
        }
        body = default;
        return false;
    }

    private static bool TryReadElement(ReadOnlySpan<byte> data, scoped ref int pos, out uint id, out ReadOnlySpan<byte> body)
    {
        id = 0;
        body = default;
        if (pos >= data.Length)
            return false;

        id = (uint)ReadVint(data, ref pos, keepMarker: true);
        var size = ReadVint(data, ref pos, keepMarker: false);

        // An unknown-size element (all size bits set) runs to the end of what we hold.
        var length = size == long.MaxValue ? data.Length - pos : (int)Math.Min(size, data.Length - pos);
        body = data.Slice(pos, length);
        pos += length;
        return true;
    }

    /// <summary>
    /// EBML variable-length integer. The number of leading zero bits in the first byte gives the total
    /// byte count; element IDs keep the length marker, sizes strip it.
    /// </summary>
    private static long ReadVint(ReadOnlySpan<byte> data, scoped ref int pos, bool keepMarker)
    {
        var first = data[pos];
        var length = 1;
        while (length <= 8 && (first & (0x80 >> (length - 1))) == 0)
            length++;
        if (length > 8)
            throw new ArgumentOutOfRangeException(nameof(data), "Invalid EBML vint");

        long value = keepMarker ? first : first & ((1 << (8 - length)) - 1);
        var allOnes = !keepMarker && (first & ((1 << (8 - length)) - 1)) == ((1 << (8 - length)) - 1);

        for (var i = 1; i < length; i++)
        {
            var b = data[pos + i];
            value = (value << 8) | b;
            allOnes &= b == 0xFF;
        }
        pos += length;

        return allOnes ? long.MaxValue : value;
    }

    private static ulong ReadUInt(ReadOnlySpan<byte> data)
    {
        ulong value = 0;
        foreach (var b in data)
            value = (value << 8) | b;
        return value;
    }
}
