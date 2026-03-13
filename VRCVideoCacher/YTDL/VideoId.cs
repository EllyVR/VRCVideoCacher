using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace VRCVideoCacher.YTDL;

public class VideoId
{
    private static readonly ILogger Log = Program.Logger.ForContext<VideoId>();
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };
    private static readonly string[] YouTubeHosts = ["youtube.com", "youtu.be", "www.youtube.com", "m.youtube.com", "music.youtube.com"];
    private static readonly Regex YoutubeRegex = new(@"(?:youtube\.com\/(?:[^\/\n\s]+\/\S+\/|(?:v|e(?:mbed)?)\/|live\/|\S*?[?&]v=)|youtu\.be\/)([a-zA-Z0-9_-]{11})");

    private static bool IsYouTubeUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return YouTubeHosts.Contains(uri.Host);
        }
        catch
        {
            return false;
        }
    }

    private static string HashUrl(string url)
    {
        return Convert.ToBase64String(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(url)))
            .Replace("/", "")
            .Replace("+", "")
            .Replace("=", "");
    }
    
    private static readonly List<string> YouTubeResolvers = new()
    {
        "dmn.moe",
        "u2b.cx",
        "t-ne.x0.to",
        "nextnex.com",
        "r.0cm.org"
    };

    public static async Task<VideoInfo?> GetVideoId(string url, bool avPro)
    {
        url = url.Trim();
        
        if (url.StartsWith("https://dmn.moe"))
        {
            url = url.Replace("/sr/", "/yt/");
            Log.Information("YTS URL detected, modified to: {URL}", url);
        }
        
        var uriObject = new Uri(url);
        if (YouTubeResolvers.Contains(uriObject.Host))
        {
            var resolvedUrl = await GetRedirectUrl(url);
            if (url != resolvedUrl)
            {
                url = resolvedUrl;
                Log.Information("YouTube resolver URL resolved to URL: {URL}", resolvedUrl);
            }
            else
            {
                Log.Error("Failed to resolve YouTube resolver URL: {URL}", url);
            }
        }

        if (url.StartsWith("http://api.pypy.dance/video") ||
            url.StartsWith("https://api.pypy.dance/video"))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, url);
                using var res = await HttpClient.SendAsync(req);
                var videoUrl = res.RequestMessage?.RequestUri?.ToString();
                if (string.IsNullOrEmpty(videoUrl))
                {
                    Log.Error("Failed to get video ID from PypyDance URL: {URL} Response: {Response} - {Data}", url, res.StatusCode, await res.Content.ReadAsStringAsync());
                    return null;
                }
                var uri = new Uri(videoUrl);
                var fileName = Path.GetFileName(uri.LocalPath);
                var pypyVideoId = !fileName.Contains('.') ? fileName : fileName.Split('.')[0];

                var pypyUri = new Uri(url);
                var query = HttpUtility.ParseQueryString(pypyUri.Query);
                int.TryParse(query.Get("id"), out var idInt);
                _ = Task.Run(async () =>
                {
                    await PyPyDanceApiService.DownloadMetadata(idInt, pypyVideoId);
                });
                

                return new VideoInfo
                {
                    VideoUrl = videoUrl,
                    VideoId = pypyVideoId,
                    UrlType = UrlType.PyPyDance,
                    DownloadFormat = DownloadFormat.MP4
                };
            }
            catch
            {
                Log.Error("Failed to get video ID from PypyDance URL: {URL}", url);
                return null;
            }
        }

        if (url.StartsWith("https://na2.vrdancing.club") ||
            url.StartsWith("https://eu2.vrdancing.club"))
        {
            var uri = new Uri(url);
            var code = Path.GetFileNameWithoutExtension(uri.LocalPath);
            var videoId = HashUrl(url);
            _ = Task.Run(async () =>
            {
                await VRDancingAPIService.DownloadMetadata(code, videoId);
            });
            return new VideoInfo
            {
                VideoUrl = url,
                VideoId = videoId,
                UrlType = UrlType.VRDancing,
                DownloadFormat = DownloadFormat.MP4
            };
        }

        if (IsYouTubeUrl(url))
        {
            var videoId = string.Empty;
            var match = YoutubeRegex.Match(url);
            if (match.Success)
            {
                videoId = match.Groups[1].Value;
            }
            else if (url.StartsWith("https://www.youtube.com/shorts/") ||
                     url.StartsWith("https://youtube.com/shorts/"))
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath;
                var parts = path.Split('/');
                videoId = parts[^1];
            }
            if (string.IsNullOrEmpty(videoId))
            {
                Log.Error("Failed to parse video ID from YouTube URL: {URL}", url);
                return null;
            }
            videoId = videoId.Length > 11 ? videoId.Substring(0, 11) : videoId;
            return new VideoInfo
            {
                VideoUrl = url,
                VideoId = videoId,
                UrlType = UrlType.YouTube,
                DownloadFormat = avPro ? DownloadFormat.Webm : DownloadFormat.MP4
            };
        }

        var urlHash = HashUrl(url);
        return new VideoInfo
        {
            VideoUrl = url,
            VideoId = urlHash,
            UrlType = UrlType.Other,
            DownloadFormat = DownloadFormat.MP4
        };
    }

    public static async Task<string> TryGetYouTubeVideoId(string url)
    {
        var additionalArgs = YtdlManager.GenerateYtdlArgs();
        var process = new Process
        {
            StartInfo =
            {
                FileName = YtdlManager.YtdlPath,
                Arguments = $"{additionalArgs} -j \"{url}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }
        };
        process.Start();
        var rawData = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new Exception($"Failed to get video ID: {error.Trim()}");
        if (string.IsNullOrEmpty(rawData))
            throw new Exception("Failed to get video ID");
        var data = JsonSerializer.Deserialize(rawData, VideoIdJsonContext.Default.YtdlpVideoInfo);
        if (data?.Id is null || data.Duration is null)
            throw new Exception("Failed to get video ID");
        
        DatabaseManager.AddVideoInfoCache(new VideoInfoCache
        {
            Id = data.Id,
            Title = data.Name,
            Author = data.Author,
            Duration = data.Duration,
            Type = UrlType.YouTube
        });

        if (data.IsLive == true)
            throw new Exception("Failed to get video ID: Video is a stream");
        if (data.Duration > ConfigManager.Config.CacheYouTubeMaxLength * 60)
            throw new Exception($"Failed to get video ID: Video is longer than configured max length ({data.Duration / 60}/{ConfigManager.Config.CacheYouTubeMaxLength})");

        return data.Id;
    }

    public static async Task<string> GetURLResonite(string url)
    {
        var process = new Process
        {
            StartInfo =
            {
                FileName = YtdlManager.YtdlPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }
        };

        var additionalArgs = YtdlManager.GenerateYtdlArgs();
        var languageArg = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
            ? string.Empty
            : $" -f [language={ConfigManager.Config.YtdlpDubLanguage}]";
        process.StartInfo.Arguments = $"--flat-playlist -i -J -s {languageArg} --impersonate=\"safari\" --extractor-args=\"youtube:player_client=web\" {additionalArgs} {url}";

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        output = output.Trim();
        var error = await process.StandardError.ReadToEndAsync();
        error = error.Trim();
        await process.WaitForExitAsync();
        Log.Information("Started yt-dlp with args: {args}", process.StartInfo.Arguments);

        if (process.ExitCode != 0)
        {
            if (error.Contains("Sign in to confirm you’re not a bot"))
                Log.Error("Fix this error by following these instructions: https://github.com/clienthax/VRCVideoCacherBrowserExtension");

            return string.Empty;
        }

        return output;
    }

    // High bitrate video (1080)
    // https://www.youtube.com/watch?v=DzQwWlbnZvo

    // 4k video
    // https://www.youtube.com/watch?v=i1csLh-0L9E

    public static async Task<Tuple<string, bool>> GetUrl(VideoInfo videoInfo, bool avPro)
    {
        // if url contains "results?" then it's a search
        if (videoInfo.VideoUrl.Contains("results?") && videoInfo.UrlType == UrlType.YouTube)
        {
            const string message = "URL is a search query, cannot get video URL.";
            return new Tuple<string, bool>(message, false);
        }

        var process = new Process
        {
            StartInfo =
            {
                FileName = YtdlManager.YtdlPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }
        };

        // yt-dlp -f best/bestvideo[height<=?720]+bestaudio --no-playlist --no-warnings --get-url https://youtu.be/GoSo8YOKSAE
        var url = videoInfo.VideoUrl;
        var additionalArgs = YtdlManager.GenerateYtdlArgs();
        var languageArg = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
            ? string.Empty
            : $"[language={ConfigManager.Config.YtdlpDubLanguage}]/(mp4/best)[height<=?1080][height>=?64][width>=?64]";

        if (avPro)
        {
            process.StartInfo.Arguments = $"-f \"(mp4/best)[height<=?1080][height>=?64][width>=?64]{languageArg}\" --impersonate=\"safari\" --extractor-args=\"youtube:player_client=web\" {additionalArgs} --get-url \"{url}\"";
        }
        else
        {
            process.StartInfo.Arguments = $"-f \"(mp4/best)[vcodec!=av01][vcodec!=vp9.2][height<=?1080][height>=?64][width>=?64][protocol^=http]\" {additionalArgs} --get-url \"{url}\"";
        }

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        output = output.Trim();
        var error = await process.StandardError.ReadToEndAsync();
        error = error.Trim();
        await process.WaitForExitAsync();
        Log.Information("Started yt-dlp with args: {args}", process.StartInfo.Arguments);

        if (process.ExitCode != 0)
        {
            if (error.Contains("Sign in to confirm you’re not a bot"))
                Log.Error("Fix this error by following these instructions: https://github.com/clienthax/VRCVideoCacherBrowserExtension");

            return new Tuple<string, bool>(error, false);
        }

        return new Tuple<string, bool>(output, true);
    }

    private static async Task<string> GetRedirectUrl(string requestUrl)
    {
        using var req = new HttpRequestMessage(HttpMethod.Head, requestUrl);
        using var res = await HttpClient.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            return requestUrl;

        return res.RequestMessage?.RequestUri?.ToString() ?? requestUrl;
    }
}