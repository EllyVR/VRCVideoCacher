using System.Buffers.Binary;

namespace VRCVideoCacher.Services.Sabr;

/// <summary>
/// The exact duration of every fragment in a track, parsed from the <c>sidx</c> box that YouTube ships
/// inside the SABR init segment (<c>ftyp + moov + sidx</c>, a few KB).
///
/// This is what lets us publish a complete, correct HLS VOD playlist — and therefore give AVPro a
/// working scrub bar — in the first round-trip, before a single byte of media is fetched. It is also
/// how YouTube's own player scrubs instantly. Guessing the durations (or waiting for the whole fetch
/// to learn them) is unnecessary: the server already told us.
///
/// Fragment durations are NOT uniform (5.0s, 5.32s, 7.32s…), so the index is not reconstructible from
/// the total duration and segment count.
/// </summary>
internal sealed class SegmentIndex
{
    public required IReadOnlyList<long> DurationsMs { get; init; }

    public long TotalDurationMs => DurationsMs.Sum();
    public int Count => DurationsMs.Count;

    /// <summary>
    /// The <c>hls_time</c> that makes ffmpeg cut exactly on this index's boundaries — which is what
    /// makes the playlist we publish from the index actually describe the segments ffmpeg writes.
    ///
    /// ffmpeg starts a new segment at the first keyframe at least <c>hls_time</c> after the last cut.
    /// H.264/fMP4 only has keyframes at fragment starts, so almost any value works. VP9/WebM has extra
    /// keyframes *inside* each cluster (every ~2s), and a small hls_time makes ffmpeg cut there instead
    /// — producing segments that don't match the playlist at all. Sitting just under the shortest
    /// fragment makes ffmpeg skip the internal keyframes and land on the real boundaries in both cases.
    ///
    /// The last fragment is excluded: it's a short remainder, not a real cadence.
    /// </summary>
    public double FfmpegSegmentSeconds
    {
        get
        {
            var cadence = Count > 1 ? DurationsMs.Take(Count - 1).Min() : DurationsMs[0];
            return Math.Round(cadence * 0.9 / 1000.0, 3);
        }
    }

    /// <summary>Start time of a segment, for turning a seek target into a SABR <c>player_time_ms</c>.</summary>
    public long StartMsOf(int index)
    {
        long start = 0;
        for (var i = 0; i < index && i < DurationsMs.Count; i++)
            start += DurationsMs[i];
        return start;
    }

    /// <summary>The segment containing <paramref name="timeMs"/>.</summary>
    public int IndexAt(long timeMs)
    {
        long start = 0;
        for (var i = 0; i < DurationsMs.Count; i++)
        {
            start += DurationsMs[i];
            if (timeMs < start)
                return i;
        }
        return Math.Max(0, DurationsMs.Count - 1);
    }

    /// <summary>
    /// Reads the timeline from the head of a track, whichever container it is in:
    /// H.264 comes as fMP4 (index in a <c>sidx</c> box) and VP9/AV1 come as WebM (index in a Matroska
    /// <c>Cues</c> element). Both ship it before the first media fragment. Null if neither is present.
    /// </summary>
    /// <param name="totalDurationMs">
    /// Needed only for WebM: Cues give each cluster's start time, so the final cluster's duration can
    /// only be derived from the total.
    /// </param>
    public static SegmentIndex? TryParse(ReadOnlySpan<byte> data, long totalDurationMs = 0)
    {
        var sidx = FindBox(data, "sidx"u8);
        if (!sidx.IsEmpty)
            return ParseSidx(sidx);

        return WebmCues.TryParse(data, totalDurationMs);
    }

    /// <summary>Returns the box's payload (contents after the 8-byte header), or empty if not found.</summary>
    private static ReadOnlySpan<byte> FindBox(ReadOnlySpan<byte> data, ReadOnlySpan<byte> type)
    {
        var offset = 0;
        while (offset + 8 <= data.Length)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (size < 8 || offset + size > data.Length)
                return default;

            if (data.Slice(offset + 4, 4).SequenceEqual(type))
                return data.Slice(offset + 8, size - 8);

            offset += size;
        }
        return default;
    }

    private static SegmentIndex? ParseSidx(ReadOnlySpan<byte> sidx)
    {
        try
        {
            var version = sidx[0];
            var pos = 4; // version + flags

            pos += 4; // reference_ID
            var timescale = BinaryPrimitives.ReadUInt32BigEndian(sidx[pos..]);
            pos += 4;
            if (timescale == 0)
                return null;

            // earliest_presentation_time + first_offset: 32-bit each in v0, 64-bit in v1.
            pos += version == 0 ? 8 : 16;

            pos += 2; // reserved
            var count = BinaryPrimitives.ReadUInt16BigEndian(sidx[pos..]);
            pos += 2;

            var durations = new long[count];
            for (var i = 0; i < count; i++)
            {
                // Each entry is 12 bytes: referenced_size, subsegment_duration, SAP flags.
                var duration = BinaryPrimitives.ReadUInt32BigEndian(sidx[(pos + 4)..]);
                durations[i] = duration * 1000L / timescale;
                pos += 12;
            }

            return new SegmentIndex { DurationsMs = durations };
        }
        catch (ArgumentOutOfRangeException)
        {
            return null; // malformed / truncated sidx
        }
    }
}
