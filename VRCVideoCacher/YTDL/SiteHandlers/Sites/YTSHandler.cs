using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class YTSHandler : ISiteHandler
{
    private static readonly ILogger Log = Program.Logger.ForContext<YTSHandler>();
    
    public bool CanHandle(Uri uri) => false; // rewrite only

    public Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro) => Task.FromResult<VideoInfo?>(null);

    public Task<string> RewriteUrl(string url, Uri uri)
    {
        if (!url.StartsWith("https://dmn.moe"))
            return Task.FromResult(url);

        var newUrl = url.Replace("/sr/", "/yt/");
        Log.Information("YTS URL detected, modified to: {URL}", newUrl);
        return Task.FromResult(newUrl);
    }

    public List<string> GetYtdlpArguments(Uri uri, bool avPro) => [];

}