using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL;

public class VideoDownloader
{
    private static readonly ILogger Log = Program.Logger.ForContext<VideoDownloader>();
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };
    private static readonly ConcurrentQueue<VideoInfo> DownloadQueue = new();
    private static readonly string TempDownloadMp4Path;
    private static readonly string TempDownloadWebmPath;

    // Events for UI
    public static event Action<VideoInfo>? OnDownloadStarted;
    public static event Action<VideoInfo, bool>? OnDownloadCompleted;
    public static event Action? OnQueueChanged;

    // Current download tracking
    private static VideoInfo? _currentDownload;

    static VideoDownloader()
    {
        TempDownloadMp4Path = Path.Join(CacheManager.CachePath, "_tempVideo.mp4");
        TempDownloadWebmPath = Path.Join(CacheManager.CachePath, "_tempVideo.webm");
        Task.Run(DownloadThread);
    }

    private static async Task DownloadThread()
    {
        while (true)
        {
            await Task.Delay(100);
            if (DownloadQueue.IsEmpty)
            {
                _currentDownload = null;
                continue;
            }

            DownloadQueue.TryDequeue(out var queueItem);
            if (queueItem == null)
                continue;

            _currentDownload = queueItem;
            OnDownloadStarted?.Invoke(queueItem);

            var success = false;
            try
            {
                switch (queueItem.UrlType)
                {
                    case UrlType.YouTube:
                        success = await DownloadYouTubeVideo(queueItem);
                        break;
                    case UrlType.PyPyDance:
                        success = await DownloadVideoWithId(queueItem);
                        break;
                    case UrlType.VRDancing:
                        success = await DownloadVRDancingVideoWithId(queueItem);
                        break;
                    case UrlType.Other:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Exception during download: {Ex}", ex.ToString());
                success = false;
            }

            OnDownloadCompleted?.Invoke(queueItem, success);
            OnQueueChanged?.Invoke();
            _currentDownload = null;
        }
    }

    public static void QueueDownload(VideoInfo videoInfo)
    {
        if (DownloadQueue.Any(x => x.VideoId == videoInfo.VideoId &&
                                   x.DownloadFormat == videoInfo.DownloadFormat))
        {
            // Log.Information("URL is already in the download queue.");
            return;
        }
        if (_currentDownload != null &&
            _currentDownload.VideoId == videoInfo.VideoId &&
            _currentDownload.DownloadFormat == videoInfo.DownloadFormat)
        {
            // Log.Information("URL is already being downloaded.");
            return;
        }

        DownloadQueue.Enqueue(videoInfo);
        OnQueueChanged?.Invoke();
    }

    public static void ClearQueue()
    {
        DownloadQueue.Clear();
        OnQueueChanged?.Invoke();
    }

    // Public accessors for UI
    public static IReadOnlyList<VideoInfo> GetQueueSnapshot() => DownloadQueue.ToArray();
    public static int GetQueueCount() => DownloadQueue.Count;
    public static VideoInfo? GetCurrentDownload() => _currentDownload;

    private static async Task<bool> DownloadYouTubeVideo(VideoInfo videoInfo)
    {
        var url = videoInfo.VideoUrl;
        string? videoId;
        try
        {
            videoId = await VideoId.TryGetYouTubeVideoId(url);
            if (string.IsNullOrEmpty(videoId))
            {
                Log.Warning("Invalid YouTube URL: {URL}", url);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Not downloading YouTube video: {URL} {ex}", url, ex.ToString());
            return false;
        }

        if (File.Exists(TempDownloadMp4Path))
        {
            Log.Warning("Temp file already exists, deleting...");
            File.Delete(TempDownloadMp4Path);
        }
        if (File.Exists(TempDownloadWebmPath))
        {
            Log.Warning("Temp file already exists, deleting...");
            File.Delete(TempDownloadWebmPath);
        }

        var args = new List<string>();
        args.Add("-q");

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

        if (videoInfo.DownloadFormat == DownloadFormat.Webm)
        {
            // process.StartInfo.Arguments = $"-q -o \"{TempDownloadMp4Path}\" -f \"bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='^(avc|h264)']+ba[ext=m4a]/bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec!=av01][vcodec!=vp9.2][protocol^=http]\" --remux-video mp4 {additionalArgs} -- \"{videoId}\"";
            var audioArg = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
                ? "+ba[acodec=opus][ext=webm]"
                : $"+(ba[acodec=opus][ext=webm][language={ConfigManager.Config.YtdlpDubLanguage}]/ba[acodec=opus][ext=webm])";
            args.Add($"-o \"{TempDownloadWebmPath}\"");
            args.Add($"-f \"bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='^av01'][ext=mp4][dynamic_range='SDR']{audioArg}/bv*[height<={ConfigManager.Config.CacheYouTubeMaxResolution}][vcodec~='vp9'][ext=webm][dynamic_range='SDR']{audioArg}\"");
        }
        else
        {
            // Potato mode.
            var audioArgPotato = string.IsNullOrEmpty(ConfigManager.Config.YtdlpDubLanguage)
                ? "+ba[ext=m4a]"
                : $"+(ba[ext=m4a][language={ConfigManager.Config.YtdlpDubLanguage}]/ba[ext=m4a])";
            args.Add($"-o \"{TempDownloadMp4Path}\"");
            args.Add($"-f \"bv*[height<=1080][vcodec~='^(avc|h264)']{audioArgPotato}/bv*[height<=1080][vcodec~='^av01'][dynamic_range='SDR']\"");
            args.Add("--remux-video mp4");
            // $@"-f best/bestvideo[height<=?720]+bestaudio {url} " %(id)s.%(ext)s
        }

        process.StartInfo.Arguments = YtdlManager.GenerateYtdlArgs(args, $"-- \"{videoId}\"");
        Log.Information("Downloading YouTube Video: {Args}", process.StartInfo.Arguments);
        process.Start();
        await process.WaitForExitAsync();
        var error = await process.StandardError.ReadToEndAsync();
        error = error.Trim();
        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download YouTube Video: {exitCode} {URL} {error}", process.ExitCode, url, error);
            if (error.Contains("Sign in to confirm you’re not a bot"))
                Log.Error("Fix this error by following these instructions: https://github.com/clienthax/VRCVideoCacherBrowserExtension");

            return false;
        }
        Thread.Sleep(100);

        var fileName = $"{videoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            try
            {
                if (File.Exists(TempDownloadMp4Path))
                    File.Delete(TempDownloadMp4Path);
                if (File.Exists(TempDownloadWebmPath))
                    File.Delete(TempDownloadWebmPath);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to delete temp file: {ex}", ex.ToString());
            }
            return false;
        }

        if (File.Exists(TempDownloadMp4Path))
        {
            File.Move(TempDownloadMp4Path, filePath);
        }
        else if (File.Exists(TempDownloadWebmPath))
        {
            File.Move(TempDownloadWebmPath, filePath);
        }
        else
        {
            Log.Error("Failed to download YouTube Video: {URL}", url);
            return false;
        }

        CacheManager.AddToCache(fileName);
        Log.Information("YouTube Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");
        return true;
    }

    private static async Task<bool> DownloadVRDancingVideoWithId(VideoInfo videoInfo)
    {
        if (File.Exists(TempDownloadMp4Path))
        {
            Log.Warning("Temp file already exists, deleting...");
            File.Delete(TempDownloadMp4Path);
        }
        if (File.Exists(TempDownloadWebmPath))
        {
            Log.Warning("Temp file already exists, deleting...");
            File.Delete(TempDownloadWebmPath);
        }

        Log.Information("Downloading Video: {URL}", videoInfo.VideoUrl);
        var url = videoInfo.VideoUrl;
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
        process.StartInfo.Arguments = $"-q -o \"{TempDownloadMp4Path}\" --remux-video mp4 {url}";
        Log.Information("Downloading VRDancing Video: {URL}", process.StartInfo.Arguments);
        process.Start();
        await process.WaitForExitAsync();
        var error = await process.StandardError.ReadToEndAsync();
        error = error.Trim();
        if (process.ExitCode != 0)
        {
            Log.Error("Failed to download YouTube Video: {exitCode} {URL} {error}", process.ExitCode, url, error);
            if (error.Contains("Sign in to confirm you’re not a bot"))
                Log.Error("Fix this error by following these instructions: https://github.com/clienthax/VRCVideoCacherBrowserExtension");

            return false;
        }
        Thread.Sleep(100);

        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (File.Exists(filePath))
        {
            Log.Error("File already exists, canceling...");
            try
            {
                if (File.Exists(TempDownloadMp4Path))
                    File.Delete(TempDownloadMp4Path);
                if (File.Exists(TempDownloadWebmPath))
                    File.Delete(TempDownloadWebmPath);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to delete temp file: {ex}", ex.ToString());
            }

            return false;
        }
        if (File.Exists(TempDownloadMp4Path))
        {
            File.Move(TempDownloadMp4Path, filePath);
        }
        else if (File.Exists(TempDownloadWebmPath))
        {
            File.Move(TempDownloadWebmPath, filePath);
        }
        else
        {
            Log.Error("Failed to download YouTube Video: {URL}", url);
            return false;
        }

        CacheManager.AddToCache(fileName);
        return true;
    }
    
    private static async Task<bool> DownloadVideoWithId(VideoInfo videoInfo)
    {
        if (File.Exists(TempDownloadMp4Path))
        {
            Log.Warning("Temp file already exists, deleting...");
            File.Delete(TempDownloadMp4Path);
        }
        if (File.Exists(TempDownloadWebmPath))
        {
            Log.Warning("Temp file already exists, deleting...");
            File.Delete(TempDownloadWebmPath);
        }

        Log.Information("Downloading Video: {URL}", videoInfo.VideoUrl);
        var url = videoInfo.VideoUrl;
        var response = await HttpClient.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            Log.Information("Redirected to: {URL}", response.Headers.Location);
            url = response.Headers.Location?.ToString();
            response = await HttpClient.GetAsync(url);
        }
        if (!response.IsSuccessStatusCode)
        {
            Log.Error("Failed to download video: {URL}", url);
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(TempDownloadMp4Path, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream);
        fileStream.Close();
        response.Dispose();
        await Task.Delay(10);

        var fileName = $"{videoInfo.VideoId}.{videoInfo.DownloadFormat.ToString().ToLower()}";
        var filePath = Path.Join(CacheManager.CachePath, fileName);
        if (File.Exists(TempDownloadMp4Path))
        {
            File.Move(TempDownloadMp4Path, filePath);
        }
        else if (File.Exists(TempDownloadWebmPath))
        {
            File.Move(TempDownloadWebmPath, filePath);
        }
        else
        {
            Log.Error("Failed to download Video: {URL}", url);
            return false;
        }

        CacheManager.AddToCache(fileName);
        Log.Information("Video Downloaded: {URL}", $"{ConfigManager.Config.YtdlpWebServerUrl}/{fileName}");
        return true;
    }
}