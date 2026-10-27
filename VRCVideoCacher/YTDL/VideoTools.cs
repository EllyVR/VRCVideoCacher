namespace VRCVideoCacher;

public class VideoTools
{
    private static readonly Serilog.ILogger Log = Program.Logger.ForContext<VideoTools>();
    private static readonly HttpClient HttpClient = new();

    public static async Task<bool> Prefetch(string videoUrl, int maxRetryCount = 7)
    {
        // If the URL is invalid, skip prefetching
        if (string.IsNullOrWhiteSpace(videoUrl) || !Uri.IsWellFormedUriString(videoUrl, UriKind.RelativeOrAbsolute))
        {
            Log.Warning("Invalid video URL provided for prefetch: {URL}", videoUrl);
            return false;
        }

        // Determine if the URL is an M3U8 playlist
        var uri = new Uri(videoUrl);
        var isM3U8 = uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || videoUrl.Contains("mime=application/vnd.apple.mpegurl");

        // Prefetch the video URL
        // - Use GET for M3U8 to extract the direct stream URL
        using var prefetchRequest = new HttpRequestMessage(isM3U8 ? HttpMethod.Get : HttpMethod.Head, videoUrl);
        using var prefetchResponse = await HttpClient.SendAsync(prefetchRequest);
        Log.Information("Video prefetch request returned status code {status}.", (int)prefetchResponse.StatusCode);

        var targetUrl = videoUrl;
        var targetName = "video";
        var statusCode = (int)prefetchResponse.StatusCode;

        if (prefetchRequest.Method == HttpMethod.Get && prefetchResponse.Content.Headers.ContentType?.MediaType == "application/vnd.apple.mpegurl")
        {
            var body = await prefetchResponse.Content.ReadAsStringAsync();
            var firstM3U8Url = body.Split('\n').FirstOrDefault(line => Uri.IsWellFormedUriString(line, UriKind.RelativeOrAbsolute));
            if (firstM3U8Url != null)
            {
                targetUrl = firstM3U8Url;
                targetName = "M3U8 stream";
                statusCode = await GetHeadStatusCode(targetUrl);
            }
        }

        if (statusCode < 400)
        {
            Log.Information("Prefetching {target} returned status code {status}, proceeding.", targetName, statusCode);
            return true;
        }

        return await RetryUntilSuccess(targetUrl, targetName, statusCode, maxRetryCount);
    }

    private static async Task<bool> RetryUntilSuccess(string targetUrl, string targetName, int statusCode, int maxRetryCount)
    {
        const int wait = 1500;
        for (var attempt = 1; statusCode >= 400 && attempt < maxRetryCount; attempt++)
        {
            Log.Warning(
                "Prefetching {target} returned status code {status}, retrying... ({attempt}/{limit})",
                targetName, statusCode, attempt + 1, maxRetryCount);
            await Task.Delay(wait);
            statusCode = await GetHeadStatusCode(targetUrl);
        }

        if (statusCode >= 400)
        {
            Log.Error(
                "Prefetching {target} failed after {limit} attempts, status code {status}. Video may not play.",
                targetName, maxRetryCount, statusCode);
            return false;
        }

        Log.Information("Prefetching {target} returned status code {status}, proceeding.", targetName, statusCode);
        return true;
    }

    private static async Task<int> GetHeadStatusCode(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        using var response = await HttpClient.SendAsync(request);
        return (int)response.StatusCode;
    }
}