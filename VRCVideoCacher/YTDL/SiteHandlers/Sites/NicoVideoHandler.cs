using System.Text.RegularExpressions;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public partial class NicoVideoHandler : ISiteHandler
{
    private static readonly string[] Hosts =
    [
        "nicovideo.jp",
        "nico.ms"
    ];

    private const string AVProFormat = "(mp4/best)[height<=?1080][height>=?64][width>=?64]";

    private const string UnityPlayerFormat =
        "(mp4/best)[vcodec!^=av01][vcodec!^=vp09][vcodec!^=vp9][height<=?1080][height>=?64][width>=?64][protocol^=http]";

    public bool CanHandle(Uri uri) => Hosts.Any(h => uri.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase));

    public Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        var cleanUrl = url.Trim().Split('?')[0].Split('#')[0];
        var (m, group) = new[]
        {
            (NicoID1().Match(cleanUrl), 4), (NicoID2().Match(cleanUrl), 2), (NicoID3().Match(cleanUrl), 2),
            (NicoID4().Match(cleanUrl), 1)
        }.FirstOrDefault(x => x.Item1.Success);

        if (m?.Success != true)
        {
            Log.Warning("Failed to parse video ID from NicoNico URL: {Url}", url);
            return Task.FromResult<VideoInfo?>(null);
        }

        var videoId = m.Groups[group].Value;

        return Task.FromResult<VideoInfo?>(new()
        {
            VideoUrl = url,
            VideoId = videoId,
            UrlType = UrlType.NicoVideo,
            DownloadFormat = DownloadFormat.MP4
        });
    }

    public List<string> GetYtdlpArguments(Uri uri, bool avPro)
    {
        var args = new List<string>
        {
            avPro ? $"-f \"{AVProFormat}\"" : $"-f \"{UnityPlayerFormat}\""
        };

        return args;
    }

    // Matches full nicovideo/niconico URLs
    [GeneratedRegex(@"^(https?)://(live|www)\.nicovideo\.jp/(watch|shorts)/(.+)$", RegexOptions.Compiled)]
    private static partial Regex NicoID1();

    [GeneratedRegex(@"^(https?)://nico\.ms/(.+)$", RegexOptions.Compiled)]
    private static partial Regex NicoID2();

    [GeneratedRegex(@"^(https?)://(www\.)?nicovideo\.jp/shorts/(.+)$", RegexOptions.Compiled)]
    private static partial Regex NicoID3();

    // Matches bare Nico video/live IDs
    [GeneratedRegex(
        @"^(sm\d+|nm\d+|am\d+|fz\d+|ut\d+|dm\d+|so\d+|ax\d+|ca\d+|cd\d+|cw\d+|fx\d+|ig\d+|na\d+|om\d+|sd\d+|sk\d+|yk\d+|yo\d+|za\d+|zb\d+|zc\d+|zd\d+|ze\d+|nl\d+|ch\d+|\d+|lv\d+|ss\d+)$",
        RegexOptions.Compiled)]
    private static partial Regex NicoID4();
}