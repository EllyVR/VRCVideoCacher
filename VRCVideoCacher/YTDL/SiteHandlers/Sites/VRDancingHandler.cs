using Serilog;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class VRDancingHandler : ISiteHandler
{
    private static readonly ILogger Log = Program.Logger.ForContext<VRDancingHandler>();
    
    private static readonly string[] Prefixes = ["https://na2.vrdancing.club", "https://eu2.vrdancing.club", "https://mpegts-beta.vrdancing.club"];

    public bool CanHandle(Uri uri) => Prefixes.Any(p => uri.ToString().StartsWith(p));
    
    public Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        var code = url.TrimEnd('/').Split('/').Last();
        var videoId = VideoId.HashUrl(url);

        _ = Task.Run(async () =>
        {
            await VRDancingAPIService.DownloadMetadata(code, videoId);
        });

        return Task.FromResult<VideoInfo?>(new VideoInfo
        {
            VideoUrl = url,
            VideoId = videoId,
            UrlType = UrlType.VRDancing,
            DownloadFormat = DownloadFormat.MP4
        });
    }

    public List<string> GetYtdlpArguments(Uri uri, bool avPro) => [];

    public Task<string> RewriteUrl(string url, Uri uri) => Task.FromResult(url);
    
}