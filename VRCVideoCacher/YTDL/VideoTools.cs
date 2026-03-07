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
        string? firstM3U8Url = null;
        using var prefetchRequest = new HttpRequestMessage(isM3U8 ? HttpMethod.Get : HttpMethod.Head, videoUrl);
        using var prefetchResponse = await HttpClient.SendAsync(prefetchRequest);
        Log.Information("Video prefetch request returned status code {status}.", (int)prefetchResponse.StatusCode);

        if (prefetchRequest.Method == HttpMethod.Get && prefetchResponse.Content.Headers.ContentType?.MediaType == "application/vnd.apple.mpegurl")
        {
            var body = await prefetchResponse.Content.ReadAsStringAsync();
            firstM3U8Url = body.Split('\n').FirstOrDefault(line => Uri.IsWellFormedUriString(line, UriKind.RelativeOrAbsolute));
        }

        if (firstM3U8Url == null)
            return true;

        // If we have an M3U8 URL, perform HEAD requests to validate accessibility
        var statusCode = 0;
        const int wait = 1500;
        for (var i = 0; i < maxRetryCount; i++)
        {
            using var m3u8Request = new HttpRequestMessage(HttpMethod.Head, firstM3U8Url);
            using var m3u8Response = await HttpClient.SendAsync(m3u8Request);
            statusCode = (int)m3u8Response.StatusCode;

            if (statusCode >= 400)
            {
                Log.Warning(
                    "Prefetching M3U8 stream returned status code {status}, retrying... ({attempt}/{limit})",
                    statusCode, i + 1, maxRetryCount);
                await Task.Delay(wait);
            }
            else
            {
                Log.Information("Prefetching M3U8 stream returned status code {status}, proceeding.", statusCode);
                break;
            }
        }

        if (statusCode != 200)
        {
            Log.Error(
                "Prefetching M3U8 stream failed after {limit} attempts, status code {status}. Video may not play.",
                maxRetryCount, statusCode);
            return false;
        }
        else
        {
            return true;
        }
    }
}