using System.Buffers.Binary;
using System.Text;
using Serilog;
using Process = System.Diagnostics.Process;

namespace VRCVideoCacher.Services.Nico;

/// <summary>
///     Muxes decrypted NicoNico video and audio fragments into a single playable fMP4 HLS segment.
/// </summary>
internal sealed class NicoSegmentMuxer(string ffmpegPath, ILogger log)
{
    /// <summary>
    ///     Muxes a video fragment and audio fragment that already match the same time span,
    ///     into a single fMP4 segment, plus the shared init segment (ftyp+moov) the playlist's EXT-X-MAP points at.
    /// </summary>
    public async Task MuxDirectSegmentAsync(byte[] videoInit, byte[] videoFragment, byte[] audioInit,
        IReadOnlyList<byte[]> audioFragments, long startMs, int sequenceNumber, string segmentPath, string initPath,
        CancellationToken ct = default)
    {
        var temp = Path.Combine(Path.GetDirectoryName(segmentPath)!, Guid.NewGuid().ToString("N"));
        var videoInput = temp + ".v";
        var audioInput = temp + ".a";
        var output = temp + ".out";

        try
        {
            await WriteConcatenatedAsync(videoInput, videoInit, [videoFragment], ct);
            var hasAudio = audioFragments.Count > 0 && audioInit.Length > 0;
            if (hasAudio)
                await WriteConcatenatedAsync(audioInput, audioInit, audioFragments, ct);

            var movFlags = hasAudio && IsWebmInit(audioInit)
                ? "+empty_moov+default_base_moof"
                : "+delay_moov+default_base_moof";

            if (hasAudio)
                await RunFfmpegAsync(
                    $"-y -loglevel error -copyts -i \"{videoInput}\" -i \"{audioInput}\" " +
                    $"-map 0:v:0 -map 1:a:0 -c copy -avoid_negative_ts disabled -f mp4 " +
                    $"-frag_duration 600000000 " +
                    $"-movflags {movFlags} \"{output}\"", ct);
            else
                await RunFfmpegAsync(
                    $"-y -loglevel error -copyts -i \"{videoInput}\" " +
                    $"-map 0:v:0 -c copy -avoid_negative_ts disabled -f mp4 " +
                    $"-frag_duration 600000000 " +
                    $"-movflags +delay_moov+default_base_moof \"{output}\"", ct);

            var muxed = await File.ReadAllBytesAsync(output, ct);
            var mediaStart = FindMoof(muxed);
            if (mediaStart <= 0)
                throw new InvalidOperationException("ffmpeg produced no fragment for the NicoNico segment");

            var init = muxed[..mediaStart];
            var media = muxed[mediaStart..];

            StampFragmentIdentity(media, init, startMs, sequenceNumber);

            if (!File.Exists(initPath))
                await WriteAtomicAsync(initPath, init, ct);

            await WriteAtomicAsync(segmentPath, media, ct);
        }
        finally
        {
            foreach (var path in new[]
                     {
                         videoInput, audioInput, output
                     })
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // ignored
                }
        }
    }

    private static async Task WriteConcatenatedAsync(string path, byte[] init, IReadOnlyList<byte[]> fragments,
        CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true);
        if (init.Length > 0)
            await fs.WriteAsync(init, ct);
        foreach (var fragment in fragments)
            if (fragment.Length > 0)
                await fs.WriteAsync(fragment, ct);
    }

    private static async Task WriteAtomicAsync(string path, byte[] data, CancellationToken ct)
    {
        var temp = path + ".part";
        await File.WriteAllBytesAsync(temp, data, ct);
        try
        {
            File.Move(temp, path, true);
        }
        catch (IOException)
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // ignored
            }
        }
    }

    private static bool IsWebmInit(byte[] init) => init is [0x1A, 0x45, 0xDF, 0xA3, ..];

    private static void StampFragmentIdentity(Span<byte> media, ReadOnlySpan<byte> init, long startMs, int sequenceNumber)
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

    private static void SetDecodeTimes(Span<byte> media, (int Start, int End) moof, Dictionary<uint, uint> timescales,
        long startMs)
    {
        foreach (var traf in Children(media[moof.Start..moof.End], "traf"u8))
        {
            var trafSpan = media.Slice(moof.Start + traf.Start, traf.End - traf.Start);

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

            var tkhdBody = trakSpan[tkhd.Start..];
            var trackId = BinaryPrimitives.ReadUInt32BigEndian(tkhdBody[(tkhdBody[0] == 1 ? 20 : 12)..]);

            var mdiaSpan = trakSpan[mdia.Start..mdia.End];
            var mdhd = Children(mdiaSpan, "mdhd"u8).FirstOrDefault();
            if (mdhd.End == 0)
                continue;

            var mdhdBody = mdiaSpan[mdhd.Start..];
            var timescale = BinaryPrimitives.ReadUInt32BigEndian(mdhdBody[(mdhdBody[0] == 1 ? 20 : 12)..]);

            result[trackId] = timescale;
        }

        return result;
    }

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
        using var process = new Process();
        process.StartInfo = new()
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8
        };

        process.Start();
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            log.Debug(
                "[nico-mux] {StartInfoFileName} {StartInfoArguments}",
                process.StartInfo.FileName,
                process.StartInfo.Arguments);
            throw new InvalidOperationException($"ffmpeg failed muxing a NicoNico segment: {stderr.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
            log.Debug("[nico-mux] {Error}", stderr.Trim());
    }
}