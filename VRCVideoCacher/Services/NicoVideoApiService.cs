using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services.Nico;

namespace VRCVideoCacher.Services;

public static partial class NicoVideoApiService
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(NicoVideoApiService));

    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private static readonly ImmutableHashSet<string> NicoVideoPrefixes =
    [
        "sm",
        "nm",
        "so"
    ];

    private static readonly ImmutableHashSet<string> NicoLivePrefixes =
    [
        "lv"
    ];

    private static readonly MemoryCache Cache = new(new MemoryCacheOptions());
    private static readonly MemoryCache LiveCache = new(new MemoryCacheOptions());

    private static readonly HttpClient HttpClient;

    static NicoVideoApiService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseCookies = false
        };
        HttpClient = new(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    [GeneratedRegex(@"<meta name=""server-response"" content=""\{(.+)\}"" />", RegexOptions.Compiled)]
    private static partial Regex ServerResponseMetaRegex();

    [GeneratedRegex(@"<script id=""embedded-data"" data-props=""(.+?)""></script>", RegexOptions.Compiled)]
    private static partial Regex EmbeddedDataScriptRegex();

    public static async Task<NicoVideoResult?> FetchVideoResult(string videoId)
    {
        if (!IsValidVideoId(videoId) || IsValidLiveId(videoId)) return null;
        if (Cache.TryGetValue(videoId, out NicoVideoResult? cached) && cached != null &&
            !string.IsNullOrEmpty(cached.Title) && !string.IsNullOrEmpty(cached.Author)) return cached;

        try
        {
            var watchUrl = $"https://www.nicovideo.jp/watch/{videoId}";

            using var getRequest = new HttpRequestMessage(HttpMethod.Get, watchUrl);
            getRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            getRequest.Headers.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            getRequest.Headers.TryAddWithoutValidation("Accept-Language", "ja,en;q=0.7,en-US;q=0.3");

            using var getResponse = await HttpClient.SendAsync(getRequest);
            if (!getResponse.IsSuccessStatusCode)
            {
                Log.Warning("NicoNico web page request failed with status code {StatusCode} for {Url}",
                    getResponse.StatusCode, watchUrl);
                return null;
            }

            var html = await getResponse.Content.ReadAsStringAsync();
            var cookieList = getResponse.Headers.TryGetValues("Set-Cookie", out var setCookies)
                ? setCookies.ToList()
                : [];

            var cookieMap = new Dictionary<string, string>();
            foreach (var parts in cookieList.Select(sc => sc.Split(';')[0]).Select(first => first.Split('=', 2))
                         .Where(parts => parts.Length == 2))
                cookieMap[parts[0].Trim()] = parts[1].Trim();

            // Extract JSON embedded in HTML
            NicoWatchPageData? pageData = null;
            var metaMatch = ServerResponseMetaRegex().Match(html);
            if (metaMatch.Success)
            {
                var jsonStr = "{" + WebUtility.HtmlDecode(metaMatch.Groups[1].Value) + "}";
                pageData = JsonSerializer.Deserialize(jsonStr, NicoJsonContext.Default.NicoWatchPageData);
            }
            else
            {
                var scriptMatch = EmbeddedDataScriptRegex().Match(html);
                if (scriptMatch.Success)
                {
                    var jsonStr = WebUtility.HtmlDecode(scriptMatch.Groups[1].Value);
                    pageData = JsonSerializer.Deserialize(jsonStr, NicoJsonContext.Default.NicoWatchPageData);
                }
            }

            if (pageData == null)
            {
                Log.Warning("Could not parse embedded NicoNico JSON metadata for {Url}", watchUrl);
                return null;
            }

            var result = new NicoVideoResult
            {
                VideoId = videoId,
                Url = watchUrl
            };

            var responseObj = pageData.Data?.Response;

            if (responseObj != null)
            {
                // Normal video
                var videoNode = responseObj.Video;
                if (videoNode != null)
                {
                    result.Title = videoNode.Title;
                    result.Description = videoNode.Description;
                    result.Duration = videoNode.Duration;
                    result.Thumbnail = videoNode.Thumbnail?.Player ?? videoNode.Thumbnail?.Url;

                    if (videoNode.Count != null)
                    {
                        result.ViewCount = videoNode.Count.View;
                        result.CommentCount = videoNode.Count.Comment;
                        result.MyListCount = videoNode.Count.MyList;
                        result.LikeCount = videoNode.Count.Like;
                    }
                }

                if (responseObj.Owner != null)
                    result.Author = responseObj.Owner.Nickname;

                if (responseObj.Tag?.Items != null)
                    result.Tags =
                    [
                        .. responseObj.Tag.Items
                            .Select(t => t.Name)
                            .Where(name => !string.IsNullOrEmpty(name))
                            .Select(name => name!)
                    ];

                // Domand media access rights (NVAPI)
                var domandNode = responseObj.Media?.Domand;
                var clientNode = responseObj.Client;
                var accessRightKey = domandNode?.AccessRightKey;
                var trackId = clientNode?.WatchTrackId;
                var nicosid = clientNode?.NicosId;

                if (!string.IsNullOrEmpty(nicosid))
                    cookieMap["nicosid"] = nicosid;

                if (!string.IsNullOrEmpty(accessRightKey) && !string.IsNullOrEmpty(trackId) && domandNode != null)
                    try
                    {
                        var audioList = new List<string>();
                        if (domandNode.Audios != null)
                            audioList.AddRange(from item in domandNode.Audios
                                where item.IsAvailable == true && !string.IsNullOrEmpty(item.Id)
                                select item.Id);

                        var videoList = new List<string>();
                        if (domandNode.Videos != null)
                            videoList.AddRange(from item in domandNode.Videos
                                where item.IsAvailable == true && !string.IsNullOrEmpty(item.Id)
                                select item.Id);

                        var outputs = new List<string[]>();
                        var firstAudio = audioList.FirstOrDefault();
                        var secondAudio = audioList.Skip(1).FirstOrDefault();

                        foreach (var vid in videoList)
                        {
                            if (firstAudio != null)
                                outputs.Add([vid, firstAudio]);
                            if (secondAudio != null)
                                outputs.Add([vid, secondAudio]);
                        }

                        if (outputs.Count > 0)
                        {
                            var postPayload = JsonSerializer.Serialize(new()
                            {
                                Outputs = outputs
                            }, NicoJsonContext.Default.NicoNvApiAccessRightsRequest);
                            var nvApiUrl =
                                $"https://nvapi.nicovideo.jp/v1/watch/{videoId}/access-rights/hls?actionTrackId={trackId}";
                            using var postRequest = new HttpRequestMessage(HttpMethod.Post, nvApiUrl);
                            postRequest.Headers.TryAddWithoutValidation("Accept",
                                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                            postRequest.Headers.TryAddWithoutValidation("Accept-Language", "ja,en;q=0.7,en-US;q=0.3");
                            postRequest.Headers.TryAddWithoutValidation("X-Access-Right-Key", accessRightKey);
                            postRequest.Headers.TryAddWithoutValidation("X-Frontend-Id", "6");
                            postRequest.Headers.TryAddWithoutValidation("X-Frontend-Version", "0");
                            postRequest.Headers.TryAddWithoutValidation("X-Niconico-Language", "ja-jp");
                            postRequest.Headers.TryAddWithoutValidation("X-Request-With", "nicovideo");
                            postRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                            var cookieHeader = string.Join("; ", cookieMap.Select(kv => $"{kv.Key}={kv.Value}"));
                            if (!string.IsNullOrEmpty(cookieHeader))
                                postRequest.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

                            postRequest.Content = new StringContent(postPayload, Encoding.UTF8, "application/json");

                            using var postResponse = await HttpClient.SendAsync(postRequest);
                            if (postResponse.IsSuccessStatusCode)
                            {
                                if (postResponse.Headers.TryGetValues("Set-Cookie", out var nvCookies))
                                    foreach (var sc in nvCookies)
                                    {
                                        var first = sc.Split(';')[0];
                                        var parts = first.Split('=', 2);
                                        if (parts.Length == 2)
                                            cookieMap[parts[0].Trim()] = parts[1].Trim();
                                    }

                                var postJsonStr = await postResponse.Content.ReadAsStringAsync();
                                var nvApiResponse = JsonSerializer.Deserialize(postJsonStr,
                                    NicoJsonContext.Default.NicoNvApiAccessRightsResponse);
                                var contentUrl = nvApiResponse?.Data?.ContentUrl;
                                if (!string.IsNullOrEmpty(contentUrl))
                                    result.StreamUrl = contentUrl;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to resolve NVAPI HLS access right for {Id}", videoId);
                    }
            }

            result.Cookies = cookieMap;
            Cache.Set(videoId, result, TimeSpan.FromMinutes(5));
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception fetching NicoNico result for {Target}", videoId);
            return null;
        }
    }

    public static async Task<NicoLiveResult?> FetchLiveResult(string liveId)
    {
        if (!IsValidLiveId(liveId)) return null;
        if (LiveCache.TryGetValue(liveId, out NicoLiveResult? cached) && cached != null &&
            !string.IsNullOrEmpty(cached.Title)) return cached;

        try
        {
            var watchUrl = $"https://live.nicovideo.jp/watch/{liveId}";

            using var getRequest = new HttpRequestMessage(HttpMethod.Get, watchUrl);
            getRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            getRequest.Headers.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            getRequest.Headers.TryAddWithoutValidation("Accept-Language", "ja,en;q=0.7,en-US;q=0.3");

            using var getResponse = await HttpClient.SendAsync(getRequest);
            if (!getResponse.IsSuccessStatusCode)
            {
                Log.Warning("NicoLive web page request failed with status code {StatusCode} for {Url}",
                    getResponse.StatusCode, watchUrl);
                return null;
            }

            var html = await getResponse.Content.ReadAsStringAsync();
            var cookieList = getResponse.Headers.TryGetValues("Set-Cookie", out var setCookies)
                ? setCookies.ToList()
                : [];

            var cookieMap = new Dictionary<string, string>();
            foreach (var parts in cookieList.Select(sc => sc.Split(';')[0]).Select(first => first.Split('=', 2))
                         .Where(parts => parts.Length == 2))
                cookieMap[parts[0].Trim()] = parts[1].Trim();

            NicoLivePageData? pageData = null;
            var metaMatch = ServerResponseMetaRegex().Match(html);
            if (metaMatch.Success)
            {
                var jsonStr = "{" + WebUtility.HtmlDecode(metaMatch.Groups[1].Value) + "}";
                pageData = JsonSerializer.Deserialize(jsonStr, NicoJsonContext.Default.NicoLivePageData);
            }
            else
            {
                var scriptMatch = EmbeddedDataScriptRegex().Match(html);
                if (scriptMatch.Success)
                {
                    var jsonStr = WebUtility.HtmlDecode(scriptMatch.Groups[1].Value);
                    pageData = JsonSerializer.Deserialize(jsonStr, NicoJsonContext.Default.NicoLivePageData);
                }
            }

            if (pageData == null)
            {
                Log.Warning("Could not parse embedded NicoLive JSON metadata for {Url}", watchUrl);
                return null;
            }

            var prog = pageData.Program;
            var site = pageData.Site;

            var result = new NicoLiveResult
            {
                LiveId = liveId,
                Url = watchUrl,
                Title = prog?.Title,
                Description = prog?.Description,
                Status = prog?.Status,
                Author = prog?.Supplier?.Name,
                WebSocketUrl = site?.Relive?.WebSocketUrl,
                FrontendId = site?.FrontendId,
                Cookies = cookieMap
            };

            if (prog?.Thumbnail != null)
                result.Thumbnail = prog.Thumbnail.Huge?.S1280X720
                                   ?? prog.Thumbnail.Huge?.S1920X1080
                                   ?? prog.Thumbnail.Huge?.S640X360
                                   ?? prog.Thumbnail.Small;

            if (prog?.Tag?.List != null)
                result.Tags =
                [
                    .. prog.Tag.List
                        .Select(t => t.Text)
                        .Where(t => !string.IsNullOrEmpty(t))
                        .Select(t => t!)
                ];

            if (prog?.Statistics != null)
            {
                result.ViewCount = prog.Statistics.WatchCount;
                result.CommentCount = prog.Statistics.CommentCount;
            }

            LiveCache.Set(liveId, result, TimeSpan.FromMinutes(2));
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception fetching NicoLive result for {Target}", liveId);
            return null;
        }
    }

    public static async Task<VideoInfoCache?> DownloadMetadata(string videoId)
    {
        try
        {
            if (IsValidLiveId(videoId))
            {
                var liveRes = await FetchLiveResult(videoId);
                if (liveRes == null) return null;

                var liveVideoInfo = new VideoInfoCache
                {
                    Id = videoId,
                    Title = liveRes.Title,
                    Author = liveRes.Author,
                    Duration = null,
                    Type = UrlType.NicoVideo
                };
                DatabaseManager.AddVideoInfoCache(liveVideoInfo);
                return liveVideoInfo;
            }

            var res = await FetchVideoResult(videoId);
            if (res == null) return null;

            var videoInfo = new VideoInfoCache
            {
                Id = videoId,
                Title = res.Title,
                Author = res.Author,
                Duration = res.Duration != null ? (int?)res.Duration.Value : null,
                Type = UrlType.NicoVideo
            };
            DatabaseManager.AddVideoInfoCache(videoInfo);
            return videoInfo;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download NicoNico metadata: {Ex}", ex.ToString());
            return null;
        }
    }

    public static async Task<string?> GetThumbnail(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        var localPath = ThumbnailManager.GetThumbnailPath(videoId);
        if (File.Exists(localPath))
            return localPath;

        string? thumbUrl;
        if (IsValidLiveId(videoId))
        {
            var liveRes = await FetchLiveResult(videoId);
            thumbUrl = liveRes?.Thumbnail;
        }
        else
        {
            var res = await FetchVideoResult(videoId);
            thumbUrl = res?.Thumbnail;
        }

        if (string.IsNullOrEmpty(thumbUrl))
            return null;

        var thumbnailPath = await ThumbnailManager.TrySaveThumbnail(videoId, thumbUrl);
        return !string.IsNullOrEmpty(thumbnailPath) ? thumbnailPath : thumbUrl;
    }

    public static async Task<VideoInfoCache?> GetVideoMetadataAsync(string videoId)
    {
        if (string.IsNullOrEmpty(videoId)) return null;

        var cachedInfo = DatabaseManager.GetVideoInfoCache(videoId);
        if (cachedInfo == null || string.IsNullOrEmpty(cachedInfo.Title) ||
            string.IsNullOrEmpty(cachedInfo.Author))
            cachedInfo = await DownloadMetadata(videoId);

        return cachedInfo;
    }

    public static bool IsValidVideoId(string videoId) =>
        NicoVideoPrefixes.Any(x => videoId.StartsWith(x, StringComparison.OrdinalIgnoreCase)) ||
        NicoLivePrefixes.Any(x => videoId.StartsWith(x, StringComparison.OrdinalIgnoreCase));

    public static bool IsValidLiveId(string videoId) =>
        NicoLivePrefixes.Any(x => videoId.StartsWith(x, StringComparison.OrdinalIgnoreCase));
}