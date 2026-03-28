using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL.SiteHandlers;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace VRCVideoCacher.YTDL;

public class VideoId
{
    private static readonly ILogger Log = Program.Logger.ForContext<VideoId>();
    
    internal static Uri? ToUri(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;

    internal static string HashUrl(string url)
    {
        return Convert.ToBase64String(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(url)))
            .Replace("/", "")
            .Replace("+", "")
            .Replace("=", "");
    }

    private static Process GetYtdlpProcess()
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
        
        return process;
    }
    
    private static async Task<(string Output, string Error, int ExitCode)> RunYtdlpAsync(List<string> args, string url)
    {
        var ytdlpProcess = GetYtdlpProcess();
        ytdlpProcess.StartInfo.Arguments = YtdlManager.GenerateYtdlArgs(args, $"\"{url}\"");
        ytdlpProcess.Start();
        var output = await ytdlpProcess.StandardOutput.ReadToEndAsync();
        var error = await ytdlpProcess.StandardError.ReadToEndAsync();
        await ytdlpProcess.WaitForExitAsync();
        return (output.Trim(), error.Trim(), ytdlpProcess.ExitCode);
    }

    public static async Task<VideoInfo?> GetVideoId(string url, bool avPro)
    {
        url = url.Trim();
        url = await SiteHandlerRegistry.ApplyRewrites(url);

        var uri = ToUri(url);
        if (uri == null) return null;

        var handler = SiteHandlerRegistry.Resolve(uri);
        return handler == null ? null : await handler.GetVideoInfo(url, uri, avPro);
    }

    public static async Task<string> TryGetYouTubeVideoId(string url)
    {
        var args = new List<string>();
        args.Add("-j");

        var (rawData, error, exitCode) = await RunYtdlpAsync(args, url);
        if (exitCode != 0)
            throw new Exception($"Failed to get video ID: {error.Trim()}");        
        
        if (string.IsNullOrEmpty(rawData))
        {
            Log.Warning("Failed to get video ID");
            return string.Empty;
        }
        var data = JsonSerializer.Deserialize(rawData, VideoIdJsonContext.Default.YtdlpVideoInfo);
        if (data?.Id is null || data.Duration is null)
        {
            Log.Warning("Failed to get video ID");
            return string.Empty;
        }

        DatabaseManager.AddVideoInfoCache(new VideoInfoCache
        {
            Id = data.Id,
            Title = data.Name,
            Author = data.Author,
            Duration = data.Duration,
            Type = UrlType.YouTube
        });

        if (data.IsLive == true)
        {
            Log.Warning("Failed to get video ID: Video is a stream");
            return string.Empty;
        }
        if (data.Duration > ConfigManager.Config.CacheYouTubeMaxLength * 60)
        {
            Log.Warning("Failed to get video ID: Video is longer than configured max length ({Length})", data.Duration / 60 / ConfigManager.Config.CacheYouTubeMaxLength);
            return string.Empty;
        }

        return data.Id;
    }

    public static async Task<string> GetURLResonite(string url)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage))
            args.Add($"-f \"[language={ConfigManager.Config.YtdlpDubLanguage}]\"");
        args.Add("--flat-playlist");
        args.Add("-i");
        args.Add("-J"); // --dump-single-json
        args.Add("-s");
        args.Add("--impersonate=\"safari\"");
        args.Add("--extractor-args=\"youtube:player_client=web\"");
        
        
        var (output, error, exitCode) = await RunYtdlpAsync(args, url);
        if (exitCode != 0)
        {
            if (error.Contains("Sign in to confirm you’re not a bot")) // Exact Text, do not modify.
                Log.Error("Fix this error by running cookie setup.");

            return string.Empty;
        }

        return output;
    }

    public static async Task<Tuple<string, bool>> GetUrl(VideoInfo videoInfo, bool avPro)
    {
        // if url contains "results?" then it's a search
        if (videoInfo.VideoUrl.Contains("results?") && videoInfo.UrlType == UrlType.YouTube)
        {
            const string message = "URL is a search query, cannot get video URL.";
            return new Tuple<string, bool>(message, false);
        }
        
        var url = videoInfo.VideoUrl;
        var uri = ToUri(url);
        var handler = uri != null ? SiteHandlerRegistry.Resolve(uri) : null;
        var args = handler?.GetYtdlpArguments(uri!, avPro) ?? [];
        args.Add("--get-url");
        
        var (output, error, exitCode) = await RunYtdlpAsync(args, url);
        Log.Information("Started yt-dlp with args: {args}", args);
        
        if (exitCode != 0)
        {
            if (error.Contains("Sign in to confirm you’re not a bot")) // Exact Text, do not modify.
                Log.Error("Fix this error by running cookie setup.");

            return new Tuple<string, bool>(error, false);
        }

        return new Tuple<string, bool>(output, true);
    }
    
}