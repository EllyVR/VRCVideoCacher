using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class GenericHandler : ISiteHandler
{
    private static readonly ILogger Log = Program.Logger.ForContext<GenericHandler>();
    
    public bool CanHandle(Uri uri) => true; // always matches, must be last in registry

    public Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        var videoId = VideoId.HashUrl(url);
        Log.Information("No specific handler found for URL, using generic handler: {URL}", url);
        return Task.FromResult<VideoInfo?>(new VideoInfo
        {
            VideoUrl = url,
            VideoId = videoId,
            UrlType = UrlType.Other,
            DownloadFormat = DownloadFormat.MP4
        });
    }
    
    public List<string> GetYtdlpArguments(Uri uri, bool avPro) => [];

    public Task<string> RewriteUrl(string url, Uri uri) => Task.FromResult(url);
    
}