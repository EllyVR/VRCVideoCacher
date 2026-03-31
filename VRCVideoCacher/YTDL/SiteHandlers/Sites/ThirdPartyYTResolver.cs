using Serilog;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class ThirdPartyYTResolver : ISiteHandler
{
    private static readonly ILogger Log = Program.Logger.ForContext<ThirdPartyYTResolver>();

    // AllowAutoRedirect=false so we can check each hop and stop as soon as
    // the destination is a URL a specific handler recognises
    private static readonly HttpClient NoAutoRedirectClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };

    public bool CanHandle(Uri uri) => false; // rewrite only

    public Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro) => Task.FromResult<VideoInfo?>(null);

    public async Task<string> RewriteUrl(string url, Uri uri)
    {
        if (SiteHandlerRegistry.HasSpecificHandler(uri))
            return url;

        const int maxHops = 5;
        var current = url;

        for (var i = 0; i < maxHops; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, current);
            using var res = await NoAutoRedirectClient.SendAsync(req);

            var location = res.Headers.Location;
            if (location == null)
                break;

            // Resolve relative redirects against the current URL
            var next = location.IsAbsoluteUri ? location.ToString() : new Uri(new Uri(current), location).ToString();

            if (!Uri.TryCreate(next, UriKind.Absolute, out var nextUri))
                break;

            // Stop as soon as the redirect target has a specific handler
            if (SiteHandlerRegistry.HasSpecificHandler(nextUri))
            {
                Log.Information("Resolved redirect: {URL} -> {Resolved}", url, next);
                return next;
            }

            current = next;

            var status = (int)res.StatusCode;
            if (status is < 300 or >= 400)
                break;
        }

        if (current != url)
        {
            Log.Information("Resolved redirect: {URL} -> {Resolved}", url, current);
            if (Uri.TryCreate(current, UriKind.Absolute, out var finalUri) && !SiteHandlerRegistry.HasSpecificHandler(finalUri))
                Log.Warning("Resolved URL has no specific handler, will use generic: {URL}", current);
        }

        return current;
    }

    public List<string> GetYtdlpArguments(Uri uri, bool avPro) => [];
}
