using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EmbedIO;
using Serilog;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services.Nico;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Services;

public static partial class NicoRestreamService
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(NicoRestreamService));

    private static readonly ConcurrentDictionary<string, INicoSession> Sessions = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<INicoSession?>>> Starting = new();
    private static readonly ConcurrentDictionary<string, (TempDir TempDir, string FilePath)> TempFiles = new();
    private static readonly ConcurrentDictionary<string, Task<string?>> TempDownloadsInFlight = new();

    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        UseCookies = false,
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private static bool _isExit;

    static NicoRestreamService()
    {
        Directory.CreateDirectory(HlsRootPath);
        CleanOrphanedSessions();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupAll();
        Task.Run(ReaperLoop);
    }

    public static string HlsRootPath { get; } = Path.Join(Program.DataPath, "nico_hls");

    private static void CleanOrphanedSessions()
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(HlsRootPath))
                try
                {
                    Directory.Delete(dir, true);
                }
                catch
                {
                    // ignore
                }
        }
        catch
        {
            // ignore
        }
    }

    private static void CleanupAll()
    {
        if (Interlocked.Exchange(ref _isExit, true)) return;
        foreach (var session in Sessions.Values)
            try
            {
                session.Dispose();
            }
            catch
            {
                // ignore
            }

        Sessions.Clear();

        foreach (var (_, (tempDir, _)) in TempFiles)
            try
            {
                tempDir.Dispose();
            }
            catch
            {
                // ignore
            }

        TempFiles.Clear();
    }

    private static async Task ReaperLoop()
    {
        while (!Volatile.Read(ref _isExit))
        {
            if (Volatile.Read(ref _isExit)) break;
            await Task.Delay(TimeSpan.FromSeconds(30));
            var now = DateTime.UtcNow;
            foreach (var (id, session) in Sessions)
            {
                var maxIdle = session is NicoLiveSession ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(15);
                if (now - session.LastAccess > maxIdle)
                    if (Sessions.TryRemove(id, out var removed))
                        try
                        {
                            removed.Dispose();
                        }
                        catch
                        {
                            // ignore
                        }
            }
        }
    }

    public static async Task<string?> GetRestreamUrlAsync(VideoInfo videoInfo)
    {
        var videoId = videoInfo.VideoId;
        var session = await EnsureSessionAsync(videoId);
        if (session == null)
            return null;

        return session.PlaybackUrl;
    }

    public static async Task EnsureAsync(string videoId, string fileName)
    {
        if (Sessions.TryGetValue(videoId, out var session))
        {
            await session.EnsureAsync(fileName);
            return;
        }

        var started = await EnsureSessionAsync(videoId);
        if (started != null)
            await started.EnsureAsync(fileName);
    }

    private static async Task<INicoSession?> EnsureSessionAsync(string videoId)
    {
        if (Sessions.TryGetValue(videoId, out var existing))
        {
            existing.Touch();
            return existing;
        }

        var lazy = Starting.GetOrAdd(videoId, id => new(async () =>
        {
            try
            {
                var muxer = new NicoSegmentMuxer(YtdlManager.FfmpegPath, Log);
                if (NicoVideoApiService.IsValidLiveId(id))
                {
                    var liveRes = await NicoVideoApiService.FetchLiveResult(id);
                    if (liveRes == null || string.IsNullOrEmpty(liveRes.WebSocketUrl))
                        return null;

                    var liveSession =
                        await NicoLiveSession.StartAsync(id, liveRes, HlsRootPath, HttpClient, muxer, Log);
                    if (liveSession != null)
                        Sessions[id] = liveSession;
                    return liveSession;
                }
                else
                {
                    var res = await NicoVideoApiService.FetchVideoResult(id);
                    if (res == null || string.IsNullOrEmpty(res.StreamUrl))
                        return null;

                    var session =
                        await NicoHlsSession.StartAsync(id, res.StreamUrl, res.Cookies, HlsRootPath, HttpClient, muxer,
                            Log);

                    Sessions[id] = session;
                    return session;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start NicoNico session for {VideoId}", id);
                return null;
            }
            finally
            {
                Starting.TryRemove(id, out _);
            }
        }, LazyThreadSafetyMode.ExecutionAndPublication));

        return await lazy.Value;
    }

    public static async Task<string?> DownloadTempVideoAsync(VideoInfo videoInfo, TimeSpan timeout)
    {
        var videoId = videoInfo.VideoId;
        // ReSharper disable once InvertIf
        if (TempFiles.TryGetValue(videoId, out var existing) && File.Exists(existing.FilePath))
        {
            var baseUrl = ConfigManager.Config.YtdlpWebServerUrl.TrimEnd('/');
            var fileExt = Path.GetExtension(existing.FilePath).TrimStart('.');
            return $"{baseUrl}/nico/temp/{videoId}.{fileExt}";
        }

        return await TempDownloadsInFlight.GetOrAdd(videoId, key => Task.Run<string?>(async () =>
        {
            try
            {
                var isWebm = videoInfo.DownloadFormat == DownloadFormat.Webm;
                var ext = isWebm ? "webm" : "mp4";
                var tempDir = new TempDir();
                var tempDownloadPath = Path.Join(tempDir.FullName, $"{key}.{ext}");

                using var process = new Process();
                process.StartInfo = new()
                {
                    FileName = YtdlManager.YtdlPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    Arguments = isWebm
                        ? $"-q -o \"{tempDownloadPath}\" --recode-video webm \"{videoInfo.VideoUrl}\""
                        : $"-q -o \"{tempDownloadPath}\" --remux-video mp4 \"{videoInfo.VideoUrl}\""
                };

                process.Start();
                await process.WaitForExitAsync();
                var error = (await process.StandardError.ReadToEndAsync()).Trim();

                if (process.ExitCode != 0 || !File.Exists(tempDownloadPath))
                {
                    tempDir.Dispose();
                    Log.Warning("Failed to download temporary NicoNico: {ExitCode} {VideoId} {Error}", process.ExitCode,
                        key, error);
                    return null;
                }

                if (TempFiles.TryRemove(key, out var old))
                    old.TempDir.Dispose();

                TempFiles[key] = (tempDir, tempDownloadPath);

                var url = $"{ConfigManager.Config.YtdlpWebServerUrl.TrimEnd('/')}/nico/temp/{key}.{ext}";
                return url;
            }
            finally
            {
                TempDownloadsInFlight.TryRemove(key, out _);
            }
        }));
    }

    public static async Task HandleTempVideoAsync(IHttpContext context, string fileName)
    {
        var videoId = Path.GetFileNameWithoutExtension(fileName);
        if (!TempFiles.TryGetValue(videoId, out var tempItem) ||
            string.IsNullOrEmpty(tempItem.FilePath) ||
            !File.Exists(tempItem.FilePath))
        {
            context.Response.StatusCode = 404;
            await SendStringWithLengthAsync(context, "Temp video not found", "text/plain");
            return;
        }

        var filePath = tempItem.FilePath;
        var fileLength = new FileInfo(filePath).Length;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var contentType = ext == ".webm" ? "video/webm" : "video/mp4";

        context.Response.ContentType = contentType;
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Headers"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";
        context.Response.Headers["Accept-Ranges"] = "bytes";

        var rangeHeader = context.Request.Headers["Range"];
        if (string.IsNullOrEmpty(rangeHeader))
        {
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = fileLength;
            await using var fs = File.OpenRead(filePath);
            await using var os = context.OpenResponseStream();
            await fs.CopyToAsync(os);
            context.SetHandled();
            return;
        }

        var match = RangeHeaderRegex().Match(rangeHeader);
        if (!match.Success)
        {
            context.Response.StatusCode = 416;
            context.Response.Headers["Content-Range"] = $"bytes */{fileLength}";
            context.SetHandled();
            return;
        }

        var start = long.Parse(match.Groups[1].Value);
        var end = match.Groups[2].Success && !string.IsNullOrEmpty(match.Groups[2].Value)
            ? long.Parse(match.Groups[2].Value)
            : fileLength - 1;

        if (start >= fileLength || end >= fileLength || start > end)
        {
            context.Response.StatusCode = 416;
            context.Response.Headers["Content-Range"] = $"bytes */{fileLength}";
            context.SetHandled();
            return;
        }

        var length = end - start + 1;
        context.Response.StatusCode = 206;
        context.Response.ContentLength64 = length;
        context.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{fileLength}";

        await using (var fs = File.OpenRead(filePath))
        await using (var os = context.OpenResponseStream())
        {
            fs.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[64 * 1024];
            var remaining = length;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = await fs.ReadAsync(buffer.AsMemory(0, toRead));
                if (read == 0) break;
                await os.WriteAsync(buffer.AsMemory(0, read));
                remaining -= read;
            }
        }

        context.SetHandled();
    }

    private static async Task SendStringWithLengthAsync(IHttpContext context, string text, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Headers"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";
        await using var os = context.OpenResponseStream();
        await os.WriteAsync(bytes);
        context.SetHandled();
    }

    [GeneratedRegex(@"bytes=(\d+)-(\d*)")]
    private static partial Regex RangeHeaderRegex();
}