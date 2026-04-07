using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL;

public class VideoId
{
    private static readonly ILogger Log = Program.Logger.ForContext<VideoId>();
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };
    private static readonly string[] YouTubeHosts = ["youtube.com", "youtu.be", "www.youtube.com"];
    private static readonly Regex YoutubeRegex = new(@"(?:youtube\.com\/(?:[^\/\n\s]+\/\S+\/|(?:v|e(?:mbed)?)\/|live\/|\S*?[?&]v=)|youtu\.be\/)([a-zA-Z0-9_-]{11})");

    // Shell metacharacters that could break argument parsing even without UseShellExecute
    private static readonly Regex UnsafeArgChars = new(@"[;&|`$(){}<>\n\r]");

    /// <summary>
    /// Validates that a URL is well-formed (http/https) and contains no characters
    /// that could inject extra arguments into a process argument string.
    /// </summary>
    private static string ValidateUrl(string url)
    {
        if (!url.StartsWith("http://", StringComparison.Ordinal) &&
            !url.StartsWith("https://", StringComparison.Ordinal))
            throw new ArgumentException($"URL must begin with http:// or https://: {url}");

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            throw new ArgumentException($"URL is not well-formed: {url}");

        if (UnsafeArgChars.IsMatch(url) || url.Contains(' '))
            throw new ArgumentException($"URL contains disallowed characters: {url}");

        return url;
    }

    /// <summary>
    /// Validates that an additional-args string contains no shell metacharacters
    /// that could inject unintended commands or arguments.
    /// </summary>
    private static string ValidateAdditionalArgs(string args)
    {
        if (string.IsNullOrEmpty(args))
            return args;

        if (UnsafeArgChars.IsMatch(args))
            throw new ArgumentException($"additionalArgs contains disallowed characters: {args}");

        return args;
    }

    private static bool IsYouTubeUrl(string url)
    {
        try
        {
            var uri = new Uri(url.Trim());
            return YouTubeHosts.Contains(uri.Host);
        }
        catch (UriFormatException)
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
    
    public static async Task<VideoInfo?> GetVideoId(string url)
    {
        url = url.Trim();
        
        if (url.StartsWith("http://jd.pypy.moe/api/v1/videos/") ||
            url.StartsWith("https://jd.pypy.moe/api/v1/videos/"))
        {
            var req = new HttpRequestMessage(HttpMethod.Head, url);
            var res = await HttpClient.SendAsync(req);
            var videoUrl = res.RequestMessage?.RequestUri?.ToString();
            if (string.IsNullOrEmpty(videoUrl))
            {
                Log.Error("Failed to get video ID from PypyDance URL: {URL}", url);
                return null;
            }
            try
            {
                var uri = new Uri(videoUrl);
                var fileName = Path.GetFileName(uri.LocalPath);
                var pypyVideoId = fileName.Split('.')[0];
                return new VideoInfo
                {
                    VideoUrl = videoUrl,
                    VideoId = pypyVideoId,
                    UrlType = UrlType.PyPyDance
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get video ID from PypyDance URL: {URL}", url);
                return null;
            }
        }
        
        if (url.StartsWith("https://na2.vrdancing.club") ||
            url.StartsWith("https://eu2.vrdancing.club"))
        {
            return new VideoInfo
            {
                VideoUrl = url,
                VideoId = HashUrl(url),
                UrlType = UrlType.VRDancing
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
                UrlType = UrlType.YouTube
            };
        }
        
        return new VideoInfo
        {
            VideoUrl = url,
            VideoId = HashUrl(url),
            UrlType = UrlType.Other
        };
    }

    public static async Task<string> TryGetYouTubeVideoId(string url)
    {
        using var process = new Process
        {
            StartInfo =
            {
                FileName = ConfigManager.Config.ytdlPath,
                Arguments = $"--no-playlist --no-warnings -j {url}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        var rawDataTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var rawData = await rawDataTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new Exception($"Failed to get video ID: {error.Trim()}");
        if (string.IsNullOrEmpty(rawData))
            throw new Exception("Failed to get video ID");
        var data = JsonConvert.DeserializeObject<dynamic>(rawData);
        if (data is null || data.id is null)
            throw new Exception("Failed to get video ID");
        if (data.is_live is true || data.was_live is true)
            throw new Exception("Failed to get video ID: Video is a stream");
        if (data.duration is null || data.duration > 3600)
            throw new Exception("Failed to get video ID: Video is too long ");

        return data.id;
    }

    public static async Task<string> GetUrl(string url, bool avPro, bool isRetry = false)
    {
        // if url contains "results?" then it's a search
        if (url.Contains("results?"))
        {
            Log.Error("URL is a search query, cannot get video URL.");
            return string.Empty;
        }

        using var process = new Process
        {
            StartInfo =
            {
                FileName = ConfigManager.Config.ytdlPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        // yt-dlp -f best/bestvideo[height<=?720]+bestaudio --no-playlist --no-warnings --get-url https://youtu.be/GoSo8YOKSAE
        var poToken = string.Empty;
        if (ConfigManager.Config.ytdlGeneratePoToken)
            poToken = await PoTokenGenerator.GetPoToken();
        if (!string.IsNullOrEmpty(poToken))
            poToken = $"po_token=web.player+{poToken}";
        
        var additionalArgs = ValidateAdditionalArgs(ConfigManager.Config.ytdlAdditionalArgs ?? string.Empty);
        var isYouTube = IsYouTubeUrl(url);
        url = ValidateUrl(url);
        if (avPro)
        {
            if (isYouTube)
                process.StartInfo.Arguments =
                    $"-f (mp4/best)[height<=?1080][height>=?64][width>=?64] --impersonate=\"safari\" --extractor-args=\"youtube:player_client=web;{poToken}\" --no-playlist --no-warnings {additionalArgs} --get-url {url}";
            else
                process.StartInfo.Arguments =
                    $"-f (mp4/best)[height<=?1080][height>=?64][width>=?64] --extractor-args=\"youtube:{poToken}\" --no-playlist --no-warnings {additionalArgs} --get-url {url}";
        }
        else
        {
            process.StartInfo.Arguments =
                $"-f (mp4/best)[vcodec!=av01][vcodec!=vp9.2][height<=?1080][height>=?64][width>=?64][protocol^=http] --extractor-args=\"youtube:{poToken}\" --no-playlist --no-warnings {additionalArgs} --get-url {url}";
        }
        
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        
        if (output.StartsWith("WARNING: ") ||
            output.StartsWith("ERROR: "))
        {
            Log.Error("YouTube Get URL: {output}", output);
            if (ConfigManager.Config.ytdlGeneratePoToken &&
                output.Contains("Sign in to confirm you’re not a bot") &&
                !isRetry)
            {
                await PoTokenGenerator.GeneratePoToken();
                Log.Information("Retrying with new POToken...");
                return await GetUrl(url, avPro, true);
            }

            return string.Empty;
        }
        
        if (process.ExitCode != 0)
        {
            Log.Error("YouTube Get URL: {error}", error);
            return string.Empty;
        }

        return output;
    }
}