using System.Globalization;
using System.Text;

namespace VRCVideoCacher.Services.Sabr;

/// <summary>
/// Writes the HLS playlist ourselves rather than letting ffmpeg do it.
///
/// ffmpeg only appends a segment once it has written it, and only emits <c>#EXT-X-ENDLIST</c> when the
/// whole video is muxed — and AVPro refuses to seek a playlist without ENDLIST, treating it as a
/// livestream (no scrub bar). Waiting for the fetch to finish would mean minutes of startup on a long
/// video. We don't have to: the segment index (<see cref="SegmentIndex"/>) gives every fragment's exact
/// duration in the first round-trip, so the finished playlist can go out before any media is fetched.
///
/// The segments this describes are built to match it exactly — see <see cref="SabrSegmentMuxer"/>.
/// </summary>
internal static class HlsPlaylist
{
    public const string PlaylistName = "index.m3u8";
    public const string InitName = "init.mp4";

    public static string SegmentName(int index) => $"seg_{index:D5}.m4s";

    /// <summary>
    /// Live segments are named by the server's absolute sequence number, not a zero-based index — the
    /// numbers run into the millions and the window slides, so there is no stable "segment 0".
    /// </summary>
    public static string LiveSegmentName(long sequence) => $"seg_{sequence}.m4s";

    /// <summary>
    /// A live (sliding-window) playlist. The inverse of <see cref="Build"/> in every respect that
    /// matters:
    ///
    /// <list type="bullet">
    /// <item><b>No <c>EXT-X-ENDLIST</c></b> — its presence is precisely what tells a player the stream
    ///   has ended. For VOD we go out of our way to emit it (AVPro will not scrub without it); here it
    ///   must never appear until the broadcast is actually over.</item>
    /// <item><b>No <c>PLAYLIST-TYPE</c></b> — <c>VOD</c> would promise the playlist never changes, and
    ///   <c>EVENT</c> would promise segments are only ever appended, which a sliding window violates.</item>
    /// <item><b><c>EXT-X-MEDIA-SEQUENCE</c> moves</b> — it is what tells the player that the segments
    ///   which rolled off the front are gone rather than renumbered.</item>
    /// </list>
    /// </summary>
    public static string BuildLive(IReadOnlyList<LiveFragment> window, int targetDurationSec)
    {
        var targetDuration = window.Count > 0
            ? (int)Math.Max(targetDurationSec, Math.Ceiling(window.Max(f => f.DurationMs) / 1000.0))
            : Math.Max(targetDurationSec, 1);

        var sb = new StringBuilder();
        sb.Append("#EXTM3U\n");
        sb.Append("#EXT-X-VERSION:7\n");
        sb.Append($"#EXT-X-TARGETDURATION:{targetDuration}\n");
        sb.Append($"#EXT-X-MEDIA-SEQUENCE:{(window.Count > 0 ? window[0].Sequence : 0)}\n");
        sb.Append("#EXT-X-INDEPENDENT-SEGMENTS\n");
        sb.Append($"#EXT-X-MAP:URI=\"{InitName}\"\n");

        foreach (var fragment in window)
        {
            var seconds = (fragment.DurationMs / 1000.0).ToString("F6", CultureInfo.InvariantCulture);
            sb.Append($"#EXTINF:{seconds},\n");
            sb.Append(LiveSegmentName(fragment.Sequence)).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>A complete VOD playlist — every segment listed, ENDLIST present — from the index alone.</summary>
    public static string Build(SegmentIndex index)
    {
        var targetDuration = (int)Math.Ceiling(index.DurationsMs.Max() / 1000.0);

        var sb = new StringBuilder();
        sb.Append("#EXTM3U\n");
        sb.Append("#EXT-X-VERSION:7\n");
        sb.Append($"#EXT-X-TARGETDURATION:{targetDuration}\n");
        sb.Append("#EXT-X-MEDIA-SEQUENCE:0\n");
        sb.Append("#EXT-X-PLAYLIST-TYPE:VOD\n");
        sb.Append("#EXT-X-INDEPENDENT-SEGMENTS\n");
        sb.Append($"#EXT-X-MAP:URI=\"{InitName}\"\n");

        for (var i = 0; i < index.Count; i++)
        {
            var seconds = (index.DurationsMs[i] / 1000.0).ToString("F6", CultureInfo.InvariantCulture);
            sb.Append($"#EXTINF:{seconds},\n");
            sb.Append(SegmentName(i)).Append('\n');
        }

        sb.Append("#EXT-X-ENDLIST\n");
        return sb.ToString();
    }
}
