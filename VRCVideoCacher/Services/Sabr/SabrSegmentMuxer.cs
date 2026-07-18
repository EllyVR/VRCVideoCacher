using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Serilog;

namespace VRCVideoCacher.Services.Sabr;

/// <summary>
/// Builds one muxed HLS segment from YouTube's own fragments.
///
/// Why not just point ffmpeg at the stream and let it segment? Because <b>where ffmpeg cuts is not
/// predictable</b>, and the playlist has to be published before any media is fetched (AVPro will not
/// seek a playlist without ENDLIST). ffmpeg starts a new segment at the first keyframe past
/// <c>hls_time</c>, and YouTube's videos have extra keyframes inside fragments — so ffmpeg cuts in
/// places the index knows nothing about, and the playlist ends up describing segments that don't exist.
/// Neither <c>hls_time</c> nor the segment muxer's <c>segment_times</c> reproduces the source
/// boundaries reliably; both were tried and both drift.
///
/// So ffmpeg is never asked to choose. It is handed exactly one segment's worth of media and produces
/// exactly that segment — boundaries correct by construction, matching the published playlist.
///
/// Serving the source fragments directly (unmuxed) would also work and need no ffmpeg at all, but it
/// requires an HLS audio rendition group, and Windows Media Foundation — which is what AVPro uses —
/// does not support those: video plays, audio is silent. Hence muxing.
///
/// Muxing also means the source containers stop mattering, so Opus and VP9/AV1 (which YouTube ships
/// only in WebM, and which HLS cannot carry as segments) are usable again.
/// </summary>
internal sealed class SabrSegmentMuxer(string ffmpegPath, ILogger log)
{
    /// <summary>
    /// Muxes video fragment <paramref name="videoFragment"/> with the audio covering the same time span
    /// into a single fMP4 segment, plus the shared init segment (<c>ftyp+moov</c>) the playlist's
    /// EXT-X-MAP points at. The init is identical for every segment, so it is only written once.
    /// </summary>
    /// <param name="sequenceNumber">
    /// This segment's position in the stream. Each segment is a separate ffmpeg output, so ffmpeg numbers
    /// every one of them fragment #1 — see <see cref="StampFragmentIdentity"/>.
    /// </param>
    public async Task MuxAsync(byte[] videoInit, byte[] videoFragment, byte[] audioInit,
        IReadOnlyList<byte[]> audioFragments, long startMs, long endMs, int sequenceNumber,
        string segmentPath, string initPath, CancellationToken ct = default)
    {
        var temp = Path.Combine(Path.GetDirectoryName(segmentPath)!, Guid.NewGuid().ToString("N"));
        var videoInput = temp + ".v";
        var audioInput = temp + ".a";
        var audioTrimmed = temp + ".at";
        var output = temp + ".out";

        try
        {
            // Init + fragments is a complete stream on its own — that is exactly how yt-dlp's own writer
            // produces a playable file.
            await WriteConcatenatedAsync(videoInput, videoInit, [videoFragment], ct);
            await WriteConcatenatedAsync(audioInput, audioInit, audioFragments, ct);

            var start = (startMs / 1000.0).ToString("F3", CultureInfo.InvariantCulture);
            var end = (endMs / 1000.0).ToString("F3", CultureInfo.InvariantCulture);

            // Pass 1: trim the audio to the segment.
            //
            // The video fragment ALREADY spans exactly this segment, so it needs no trimming — but the
            // audio fragments are longer (~10s vs ~5s) and straddle it, so they must be cut to size or
            // every segment carries overlapping audio and the player stalls.
            //
            // This has to be a separate pass with the trim on the OUTPUT side. Trimming on the input side
            // (-ss before -i) silently does nothing here: the audio is WebM, and its Cues point at cluster
            // offsets in YouTube's original file, which do not match the init+fragments we reconstruct — so
            // ffmpeg's seek fails and hands back the whole fragment.
            await RunFfmpegAsync(
                $"-y -loglevel error -copyts -i \"{audioInput}\" -map 0:a:0 -c copy " +
                $"-ss {start} -to {end} -avoid_negative_ts disabled -f matroska \"{audioTrimmed}\"", ct);

            // Pass 2: mux. No -ss/-to — both inputs now hold exactly the segment's media.
            // -movflags: fragmented output — ftyp+moov (the init) followed by moof+mdat (the segment).
            //
            // NOT +frag_keyframe: that starts a new fragment at every keyframe, and YouTube's video
            // fragments contain keyframes INSIDE them — so a segment would come out as two moof boxes.
            // Each one then has to be placed on the timeline separately, and a single tfdt/mfhd per
            // segment cannot describe both: the second fragment ends up claiming the same start time as
            // the first, the decoder sees overlapping fragments, and video dies while audio plays on.
            // -frag_duration (600s, in microseconds) far exceeds any segment, keeping it to one fragment.
            await RunFfmpegAsync(
                $"-y -loglevel error -copyts -i \"{videoInput}\" -i \"{audioTrimmed}\" " +
                $"-map 0:v:0 -map 1:a:0 -c copy -avoid_negative_ts disabled -f mp4 " +
                $"-frag_duration 600000000 " +
                $"-movflags +empty_moov+default_base_moof \"{output}\"", ct);

            var muxed = await File.ReadAllBytesAsync(output, ct);
            var mediaStart = FindMoof(muxed);
            if (mediaStart <= 0)
                throw new SabrException("ffmpeg produced no fragment for the segment");

            var init = muxed[..mediaStart];
            var media = muxed[mediaStart..];

            StampFragmentIdentity(media, init, startMs, sequenceNumber);

            if (!File.Exists(initPath))
                await WriteAtomicAsync(initPath, init, ct);

            await WriteAtomicAsync(segmentPath, media, ct);
        }
        finally
        {
            foreach (var path in new[] { videoInput, audioInput, audioTrimmed, output })
                try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Muxes complete, already-fetched tracks into a single playable file — the cached copy, produced
    /// from the very fragments we streamed, so a SABR video is fetched once rather than twice.
    ///
    /// H.264/VP9 + Opus in MP4 is deliberate: the HLS segments AVPro already plays are exactly that, so
    /// the cached file needs no separate AAC fetch. Note it inherits the same caveat — where a machine's
    /// Opus codec is broken or absent, Media Foundation plays this file with silent audio. That is a
    /// codec-verification problem, not a reason to switch the muxer to AAC (a pre-existing AVPro AAC bug
    /// rules that out); see the SABR section of CLAUDE.md.
    /// </summary>
    public async Task MuxCompleteAsync(string videoTrackPath, string audioTrackPath, string outputPath,
        CancellationToken ct = default)
    {
        // +faststart puts the moov up front so the file is seekable from the first byte over HTTP.
        await RunFfmpegAsync(
            $"-y -loglevel error -i \"{videoTrackPath}\" -i \"{audioTrackPath}\" " +
            $"-map 0:v:0 -map 1:a:0 -c copy -movflags +faststart \"{outputPath}\"", ct);
    }

    private static async Task WriteConcatenatedAsync(string path, byte[] init, IReadOnlyList<byte[]> fragments,
        CancellationToken ct)
    {
        await using var stream = File.Create(path);
        await stream.WriteAsync(init, ct);
        foreach (var fragment in fragments)
            await stream.WriteAsync(fragment, ct);
    }

    private static async Task WriteAtomicAsync(string path, byte[] data, CancellationToken ct)
    {
        // Write-then-move, so a request never reads a half-written file.
        var temp = path + ".part";
        await File.WriteAllBytesAsync(temp, data, ct);
        try { File.Move(temp, path, overwrite: true); }
        catch (IOException) { try { File.Delete(temp); } catch { /* raced */ } }
    }

    /// <summary>
    /// Tells each fragment where it really belongs in the stream. Sizes are unchanged, so this is an
    /// in-place patch — no boxes move.
    ///
    /// Both fixes exist because every segment is a SEPARATE ffmpeg output, and ffmpeg therefore believes
    /// each one is a whole movie starting at zero:
    ///
    ///  * <c>tfdt</c> (baseMediaDecodeTime) is rebased to 0, so every segment claims to start at t=0. No
    ///    combination of -copyts / -output_ts_offset / -avoid_negative_ts prevents it.
    ///  * <c>mfhd</c> sequence_number is 1 in every segment, i.e. each one announces itself as the first
    ///    fragment of the movie. The spec requires it to increase across the movie's fragments.
    ///
    /// Sequential playback survives both (the player just concatenates), which is what makes them so easy
    /// to miss — but a decoder re-establishing state after a SEEK does not: it cannot place the media on
    /// the timeline, and video dies while audio keeps going.
    /// </summary>
    private static void StampFragmentIdentity(Span<byte> media, ReadOnlySpan<byte> init, long startMs,
        int sequenceNumber)
    {
        var timescales = ReadTimescales(init);

        foreach (var moof in Children(media, "moof"u8))
        {
            var moofSpan = media.Slice(moof.Start, moof.End - moof.Start);

            var mfhd = Children(moofSpan, "mfhd"u8).FirstOrDefault();
            if (mfhd.End != 0)
                BinaryPrimitives.WriteUInt32BigEndian(moofSpan[(mfhd.Start + 4)..], (uint)sequenceNumber);

            SetDecodeTimes(media, moof, timescales, startMs);
        }
    }

    private static void SetDecodeTimes(Span<byte> media, (int Start, int End) moof,
        Dictionary<uint, uint> timescales, long startMs)
    {
        foreach (var traf in Children(media[moof.Start..moof.End], "traf"u8))
        {
            var trafSpan = media.Slice(moof.Start + traf.Start, traf.End - traf.Start);

            // tfhd carries the track id, which tells us which timescale the decode time is in.
            var tfhd = Children(trafSpan, "tfhd"u8).FirstOrDefault();
            if (tfhd.End == 0)
                continue;
            var trackId = BinaryPrimitives.ReadUInt32BigEndian(trafSpan[(tfhd.Start + 4)..]);
            if (!timescales.TryGetValue(trackId, out var timescale) || timescale == 0)
                continue;

            var tfdt = Children(trafSpan, "tfdt"u8).FirstOrDefault();
            if (tfdt.End == 0)
                continue;

            var value = (ulong)(startMs * timescale / 1000);
            var body = trafSpan[tfdt.Start..];
            var version = body[0];
            if (version == 1)
                BinaryPrimitives.WriteUInt64BigEndian(body[4..], value);
            else
                BinaryPrimitives.WriteUInt32BigEndian(body[4..], (uint)value);
        }
    }

    /// <summary>track_id -> media timescale, from the init segment's moov.</summary>
    private static Dictionary<uint, uint> ReadTimescales(ReadOnlySpan<byte> init)
    {
        var result = new Dictionary<uint, uint>();

        var moov = Children(init, "moov"u8).FirstOrDefault();
        if (moov.End == 0)
            return result;
        var moovSpan = init.Slice(moov.Start, moov.End - moov.Start);

        foreach (var trak in Children(moovSpan, "trak"u8))
        {
            var trakSpan = moovSpan[trak.Start..trak.End];

            var tkhd = Children(trakSpan, "tkhd"u8).FirstOrDefault();
            var mdia = Children(trakSpan, "mdia"u8).FirstOrDefault();
            if (tkhd.End == 0 || mdia.End == 0)
                continue;

            // tkhd: version(1) flags(3) creation(4/8) modification(4/8) track_id(4)
            var tkhdBody = trakSpan[tkhd.Start..];
            var trackId = BinaryPrimitives.ReadUInt32BigEndian(tkhdBody[(tkhdBody[0] == 1 ? 20 : 12)..]);

            var mdiaSpan = trakSpan[mdia.Start..mdia.End];
            var mdhd = Children(mdiaSpan, "mdhd"u8).FirstOrDefault();
            if (mdhd.End == 0)
                continue;

            // mdhd: version(1) flags(3) creation(4/8) modification(4/8) timescale(4)
            var mdhdBody = mdiaSpan[mdhd.Start..];
            var timescale = BinaryPrimitives.ReadUInt32BigEndian(mdhdBody[(mdhdBody[0] == 1 ? 20 : 12)..]);

            result[trackId] = timescale;
        }

        return result;
    }

    /// <summary>Direct children of a box body with the given type. Start is the box's PAYLOAD offset.</summary>
    private static List<(int Start, int End)> Children(ReadOnlySpan<byte> data, ReadOnlySpan<byte> type)
    {
        var found = new List<(int, int)>();
        var offset = 0;
        while (offset + 8 <= data.Length)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (size < 8 || offset + size > data.Length)
                break;
            if (data.Slice(offset + 4, 4).SequenceEqual(type))
                found.Add((offset + 8, offset + size));
            offset += size;
        }
        return found;
    }

    /// <summary>Offset of the first <c>moof</c> — everything before it is the init segment.</summary>
    private static int FindMoof(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        while (offset + 8 <= data.Length)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (size < 8 || offset + size > data.Length)
                return -1;
            if (data.Slice(offset + 4, 4).SequenceEqual("moof"u8))
                return offset;
            offset += size;
        }
        return -1;
    }

    private async Task RunFfmpegAsync(string arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo =
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
            },
        };

        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new SabrException($"ffmpeg failed muxing a segment: {stderr.Trim()}");
        if (!string.IsNullOrWhiteSpace(stderr))
            log.Debug("[sabr-mux] {Error}", stderr.Trim());
    }
}
