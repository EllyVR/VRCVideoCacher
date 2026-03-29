using VRCVideoCacher.YTDL.SiteHandlers.Sites;

namespace VRCVideoCacher.YTDL.SiteHandlers;

public static class SiteHandlerRegistry
{
    private static readonly List<ISiteHandler> Handlers =
    [
        new YouTubeHandler(),
        new PyPyDanceHandler(),
        new VRDancingHandler(),
        // fallthrough last
        new GenericHandler(),
    ];
    
    // Rewriters run first, in order, before handler resolution
    private static readonly List<ISiteHandler> Rewriters =
    [
        new NicoVideoHandler(),   // rewrites nico.ms → nicovideo.life
        new YTSHandler(),      // rewrites /sr/ → /yt/
        new ThirdPartyYTResolver(), // dmn.moe, u2b.cx etc → real YT url
    ];
    
    public static async Task<string> ApplyRewrites(string url)
    {
        var uri = VideoId.ToUri(url);
        if (uri == null) return url;
        foreach (var rewriter in Rewriters)
            url = await rewriter.RewriteUrl(url, uri);
        return url;
    }

    public static ISiteHandler? Resolve(Uri uri) => Handlers.FirstOrDefault(h => h.CanHandle(uri));
}