using System.Text.RegularExpressions;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class YouTubeHandler : ISiteHandler
{
    private static readonly ILogger Log = Program.Logger.ForContext<YouTubeHandler>();
    private static readonly string[] Hosts = ["youtube.com", "youtu.be", "www.youtube.com", "m.youtube.com", "music.youtube.com"];
    private static readonly Regex IdRegex = new(@"(?:youtube\.com\/(?:[^\/\n\s]+\/\S+\/|(?:v|e(?:mbed)?)\/|live\/|\S*?[?&]v=)|youtu\.be\/)([a-zA-Z0-9_-]{11})");
    private const string AVProFormat = "(mp4/best)[height<=?1080][height>=?64][width>=?64]";
    private const string UnityPlayerFormat = "(mp4/best)[vcodec!=av01][vcodec!=vp9.2][height<=?1080][height>=?64][width>=?64][protocol^=http]";

    public bool CanHandle(Uri uri) => Hosts.Contains(uri.Host);
    
    public Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        string? videoId = null;

        var match = IdRegex.Match(url);
        if (match.Success)
            videoId = match.Groups[1].Value;
        else if (uri.AbsolutePath.StartsWith("/shorts/"))
            videoId = uri.AbsolutePath.Split('/')[^1];

        if (string.IsNullOrEmpty(videoId))
        {
            Log.Warning("Failed to parse video ID from YouTube URL: {URL}", url);
            return Task.FromResult<VideoInfo?>(null);
        }

        videoId = videoId.Length > 11 ? videoId[..11] : videoId;

        return Task.FromResult<VideoInfo?>(new VideoInfo
        {
            VideoUrl = url,
            VideoId = videoId,
            UrlType = UrlType.YouTube,
            DownloadFormat = avPro ? DownloadFormat.Webm : DownloadFormat.MP4
        });
    }
    
    public List<string> GetYtdlpArguments(Uri uri, bool avPro)
    {
        // Using the Safari impersonation with the web client gets us the muxed m3u8's that aren't normally available otherwise.
        // These are the only streams that are compatible with AVPro currently due to WMF being unmaintained garbage.

        var args = new List<string>
        {
            "--impersonate=\"safari\""
        };
        
        if (avPro)
        {
            args.Add("--extractor-args=\"youtube:player_client=web\"");

            // AVPro
            var lang = ConfigManager.Config.YtdlpDubLanguage;
            // If dub language is set, attempt to fetch, else use defaults.
            args.Add(!string.IsNullOrEmpty(lang)
                ? $"-f \"[language={lang}]/{AVProFormat}\""
                : $"-f \"{AVProFormat}\"");
        }
        else
        {
            // Unity Player
            args.Add($"-f \"{UnityPlayerFormat}\"");
        }

        return args;
    }
    
}