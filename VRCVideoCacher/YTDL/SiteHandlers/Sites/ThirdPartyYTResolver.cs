using Serilog;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class ThirdPartyYTResolver : ISiteHandler
{
    private static readonly ILogger Log = Program.Logger.ForContext<ThirdPartyYTResolver>();
    
    private static readonly string[] Hosts = 
    [
        "dmn.moe",
        "u2b.cx",
        "t-ne.x0.to",
        "nextnex.com",
        "r.0cm.org"
    ];
    
    public bool CanHandle(Uri uri) => false; // rewrite only

    public Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro) => Task.FromResult<VideoInfo?>(null);
    
    public async Task<string> RewriteUrl(string url, Uri uri)
    {
        if (!Hosts.Contains(uri.Host))
            return url;

        using var req = new HttpRequestMessage(HttpMethod.Head, url);
        using var res = await HttpUtil.HttpClient.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            return url;

        var resolved = res.RequestMessage?.RequestUri?.ToString() ?? url;
        if (resolved != url)
        {
            Log.Information("YouTube resolver URL resolved to URL: {URL}", resolved);
            return resolved;
        }

        Log.Error("Failed to resolve YouTube resolver URL: {URL}", url);
        return url;
    }

    public List<string> GetYtdlpArguments(Uri uri, bool avPro) => [];

}