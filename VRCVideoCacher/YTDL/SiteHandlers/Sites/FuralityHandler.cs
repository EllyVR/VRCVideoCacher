using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class FuralityHandler : ISiteHandler
{
    private static readonly string[] Prefixes = new[] { "https://stream.furality.online/" };
    private static readonly ILogger Logger = Log.ForContext<FuralityHandler>();
    public bool CanHandle(Uri uri) => Prefixes.Contains(uri.ToString());

    public Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        var videoId = VideoId.HashUrl(url);
        Log.Information("Furality Url Fix, using generic handler: {URL}", url);
        return Task.FromResult<VideoInfo?>(new VideoInfo
        {
            VideoUrl = url,
            VideoId = videoId,
            UrlType = UrlType.Other,
            DownloadFormat = DownloadFormat.MP4
        });
    }

    public Task<string> RewriteUrl(string url, Uri uri)
    {
        return Task.FromResult(url);
    }
}