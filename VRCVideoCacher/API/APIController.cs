using System.Text;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VRCVideoCacher.Models;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.API;

public class ApiController : WebApiController
{
    private static readonly Serilog.ILogger Log = Program.Logger.ForContext<ApiController>();
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };

    [Route(HttpVerbs.Post, "/youtube-cookies")]
    public async Task ReceiveYoutubeCookies()
    {
        using var reader = new StreamReader(HttpContext.OpenRequestStream(), Encoding.UTF8);
        var cookies = await reader.ReadToEndAsync();
        if (!Program.IsCookiesValid(cookies))
        {
            Log.Error("Invalid cookies received, maybe you haven't logged in yet, not saving.");
            HttpContext.Response.StatusCode = 400;
            await HttpContext.SendStringAsync("Invalid cookies.", "text/plain", Encoding.UTF8);
            return;
        }
        
        await File.WriteAllTextAsync(YtdlManager.CookiesPath, cookies);

        HttpContext.Response.StatusCode = 200;
        await HttpContext.SendStringAsync("Cookies received.", "text/plain", Encoding.UTF8);

        Log.Information("Received Youtube cookies from browser extension.");
        if (!ConfigManager.Config.ytdlUseCookies) 
            Log.Warning("Config is NOT set to use cookies from browser extension.");
    }

    [Route(HttpVerbs.Get, "/getvideo")]
    public async Task GetVideo()
    {
        // escape double quotes for our own safety
        var requestUrl = Request.QueryString["url"]?.Replace("\"", "%22").Trim();
        var avPro = string.Compare(Request.QueryString["avpro"], "true", StringComparison.OrdinalIgnoreCase) == 0;
        var source = Request.QueryString["source"];
        
        if (string.IsNullOrEmpty(requestUrl))
        {
            Log.Error("No URL provided.");
            await HttpContext.SendStringAsync("No URL provided.", "text/plain", Encoding.UTF8);
            return;
        }

        Log.Information("Request URL: {URL}", requestUrl);

        if (requestUrl.StartsWith("https://dmn.moe"))
        {
            requestUrl = requestUrl.Replace("/sr/", "/yt/");
            Log.Information("YTS URL detected, modified to: {URL}", requestUrl);
            var resolvedUrl = await GetRedirectUrl(requestUrl);
            if (!string.IsNullOrEmpty(resolvedUrl))
            {
                requestUrl = resolvedUrl;
                Log.Information("YTS URL resolved to URL: {URL}", resolvedUrl);
            }
            else
            {
                Log.Error("Failed to resolve YTS URL: {URL}", requestUrl);
            }
        }

        if (ConfigManager.Config.BlockedUrls.Any(blockedUrl => requestUrl.StartsWith(blockedUrl)))
        {
            Log.Warning("URL Is Blocked: {url}", requestUrl);
            requestUrl = ConfigManager.Config.BlockRedirect;
        }

        var videoInfo = await VideoId.GetVideoId(requestUrl, avPro);
        if (videoInfo == null)
        {
            Log.Information("Failed to get Video Info for URL: {URL}", requestUrl);
            return;
        }

        var (isCached, filePath, fileName) = GetCachedFile(videoInfo.VideoId, avPro);
        if (isCached)
        {
            File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow);
            var url = $"{ConfigManager.Config.ytdlWebServerURL}/{fileName}";
            Log.Information("Responding with Cached URL: {URL}", url);
            await HttpContext.SendStringAsync(url, "text/plain", Encoding.UTF8);
            return;
        }

        if (string.IsNullOrEmpty(videoInfo.VideoId))
        {
            Log.Information("Failed to get Video ID: Bypassing.");
            await HttpContext.SendStringAsync(string.Empty, "text/plain", Encoding.UTF8);
            return;
        }

        if (requestUrl.StartsWith("https://mightygymcdn.nyc3.cdn.digitaloceanspaces.com"))
        {
            Log.Information("URL Is Mighty Gym: Bypassing.");
            await HttpContext.SendStringAsync(string.Empty, "text/plain", Encoding.UTF8);
            return;
        }

        if (source == "resonite")
        {
            Log.Information("Request sent from resonite sending json.");
            await HttpContext.SendStringAsync(await VideoId.GetURLResonite(requestUrl), "text/plain", Encoding.UTF8);
            return;
        }

        if (ConfigManager.Config.CacheYouTubeMaxResolution <= 360)
            avPro = false; // disable browser impersonation when it isn't needed

        // pls no villager
        if (requestUrl.StartsWith("https://anime.illumination.media"))
            avPro = true;
        else if (requestUrl.Contains(".imvrcdn.com") ||
                 (requestUrl.Contains(".illumination.media") && !requestUrl.StartsWith("https://yt.illumination.media")))
        {
            Log.Information("URL Is Illumination media: Bypassing.");
            await HttpContext.SendStringAsync(string.Empty, "text/plain", Encoding.UTF8);
            return;
        }

        // bypass vfi - cinema 
        if (requestUrl.StartsWith("https://virtualfilm.institute"))
        {
            Log.Information("URL Is VFI -Cinema: Bypassing.");
            await HttpContext.SendStringAsync(string.Empty, "text/plain", Encoding.UTF8);
            return;
        }

        var (response, success) = await VideoId.GetUrl(videoInfo, avPro);
        if (!success)
        {
            Log.Error("Get URL: {error}", response);
            // only send the error back if it's for YouTube, otherwise let it play the request URL normally
            if (videoInfo.UrlType == UrlType.YouTube)
            {
                HttpContext.Response.StatusCode = 500;
                await HttpContext.SendStringAsync(response, "text/plain", Encoding.UTF8);
                return;
            }
            response = string.Empty;
        }

        if ((videoInfo.UrlType == UrlType.YouTube ||
             videoInfo.VideoUrl.StartsWith("https://manifest.googlevideo.com") ||
             videoInfo.VideoUrl.Contains("googlevideo.com")) &&
            ConfigManager.Config.ytdlDelay > 0)
        {
            Log.Information("Delaying YouTube URL response for configured {delay} seconds, this can help with video errors, don't ask why", ConfigManager.Config.ytdlDelay);
            await Task.Delay(ConfigManager.Config.ytdlDelay * 1000);
        }

        Log.Information("Responding with URL: {URL}", response);
        await HttpContext.SendStringAsync(response, "text/plain", Encoding.UTF8);
        // check if file is cached again to handle race condition
        (isCached, _, _) = GetCachedFile(videoInfo.VideoId, avPro);
        if (!isCached)
            VideoDownloader.QueueDownload(videoInfo);
    }

    private static (bool isCached, string filePath, string fileName) GetCachedFile(string videoId, bool avPro)
    {
        var ext = avPro ? "webm" : "mp4";
        var fileName = $"{videoId}.{ext}";
        var filePath = Path.Combine(CacheManager.CachePath, fileName);
        var isCached = File.Exists(filePath);
        if (avPro && !isCached)
        {
            // retry with .mp4
            fileName = $"{videoId}.mp4";
            filePath = Path.Combine(CacheManager.CachePath, fileName);
            isCached = File.Exists(filePath);
        }
        return (isCached, filePath, fileName);
    }

    private static async Task<string?> GetRedirectUrl(string requestUrl)
    {
        var req = new HttpRequestMessage(HttpMethod.Head, requestUrl);
        var res = await HttpClient.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            return null;

        return res.RequestMessage?.RequestUri?.ToString();
    }
}