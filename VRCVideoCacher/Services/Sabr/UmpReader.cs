using System.Runtime.CompilerServices;

namespace VRCVideoCacher.Services.Sabr;

internal enum UmpPartId
{
    MediaHeader = 20,
    Media = 21,
    MediaEnd = 22,
    FormatInitializationMetadata = 42,
    SabrRedirect = 43,
    SabrError = 44,
    SabrSeek = 45,
    ReloadPlayerResponse = 46,
    NextRequestPolicy = 35,
    StreamProtectionStatus = 58,
}

internal readonly record struct UmpPart(int PartId, byte[] Payload);

/// <summary>
/// Reads a UMP (Ultra Media Protocol) stream: a flat sequence of (varint part_type, varint size,
/// payload) with no framing beyond that. The varint is NOT protobuf's — the byte count is encoded in
/// the high bits of the first byte, and the payload is little-endian. Ported from yt-dlp's
/// <c>_streaming/ump.py</c>.
/// </summary>
internal static class UmpReader
{
    public static async IAsyncEnumerable<UmpPart> ReadPartsAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        // The varint is read a byte at a time, so an unbuffered network stream would be a syscall per byte.
        await using var buffered = new BufferedStream(stream, 64 * 1024);
        while (true)
        {
            var partId = await ReadVarintAsync(buffered, ct);
            if (partId < 0)
                yield break; // clean end of response

            var size = await ReadVarintAsync(buffered, ct);
            if (size < 0)
                throw new EndOfStreamException("UMP stream ended while reading a part size");

            var payload = new byte[size];
            await buffered.ReadExactlyAsync(payload, ct);
            yield return new UmpPart((int)partId, payload);
        }
    }

    /// <summary>Returns -1 at end of stream, which is the only legal place for the stream to end.</summary>
    private static async ValueTask<long> ReadVarintAsync(Stream stream, CancellationToken ct)
    {
        var first = await ReadByteAsync(stream, ct);
        if (first < 0)
            return -1;

        var size = VarintSize((byte)first);
        long result = 0;
        var shift = 0;

        if (size != 5)
        {
            // The unused low bits of the first byte carry the low bits of the value.
            shift = 8 - size;
            result = first & ((1 << shift) - 1);
        }

        for (var i = 1; i < size; i++)
        {
            var next = await ReadByteAsync(stream, ct);
            if (next < 0)
                return -1;
            result |= (long)next << shift;
            shift += 8;
        }

        return result;
    }

    private static int VarintSize(byte first) =>
        first < 128 ? 1 : first < 192 ? 2 : first < 224 ? 3 : first < 240 ? 4 : 5;

    private static async ValueTask<int> ReadByteAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[1];
        var read = await stream.ReadAsync(buffer.AsMemory(0, 1), ct);
        return read == 0 ? -1 : buffer[0];
    }

    /// <summary>
    /// MEDIA and MEDIA_END payloads are prefixed with a plain protobuf varint header_id that
    /// correlates them with the MEDIA_HEADER that described the segment.
    /// </summary>
    public static (uint HeaderId, ReadOnlyMemory<byte> Data) SplitMediaPayload(byte[] payload)
    {
        var reader = new ProtoReader(payload);
        var headerId = (uint)reader.ReadVarint();
        var consumed = MediaVarintLength(payload);
        return (headerId, payload.AsMemory(consumed));
    }

    private static int MediaVarintLength(byte[] payload)
    {
        var length = 0;
        while (length < payload.Length && (payload[length] & 0x80) != 0)
            length++;
        return length + 1;
    }
}
