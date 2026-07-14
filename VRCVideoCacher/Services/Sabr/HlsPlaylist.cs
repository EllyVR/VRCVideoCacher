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
