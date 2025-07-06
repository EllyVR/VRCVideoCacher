using System.IO.Compression;
using Newtonsoft.Json;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL;

public class YtdlManager
{
    private static readonly ILogger Log = Program.Logger.ForContext<YtdlManager>();
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };
    private static readonly string YtdlVersionPath;
    private const string YtdlpApiUrl = "https://api.github.com/repos/yt-dlp/yt-dlp-nightly-builds/releases/latest";
    private const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
    
    static YtdlManager()
    {
        YtdlVersionPath = Path.Combine(Program.CurrentProcessPath, "yt-dlp.version.txt");
    }

    public static async Task TryDownloadYtdlp()
    {
        Log.Information("Checking for YT-DLP updates...");
        var response = await HttpClient.GetAsync(YtdlpApiUrl);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Failed to check for YT-DLP updates.");
            return;
        }
        var data = await response.Content.ReadAsStringAsync();
        var json = JsonConvert.DeserializeObject<YtApi>(data);
        if (json == null)
        {
            Log.Error("Failed to parse YT-DLP update response.");
            return;
        }
        var currentYtdlVersion = string.Empty;
        if (File.Exists(YtdlVersionPath))
            currentYtdlVersion = await File.ReadAllTextAsync(YtdlVersionPath);
        if (string.IsNullOrEmpty(currentYtdlVersion))
            currentYtdlVersion = "Not Installed";
        
        var latestVersion = json.tag_name;
        Log.Information("YT-DLP latest version: {Latest} Current version: {Installed}", latestVersion, currentYtdlVersion);
        if (string.IsNullOrEmpty(latestVersion))
        {
            Log.Warning("Failed to check for YT-DLP updates.");
            return;
        }
        if (!File.Exists(ConfigManager.Config.ytdlPath))
        {
            Log.Information("YT-DLP is not installed. Downloading...");
            await DownloadYtdl(json);
            await File.WriteAllTextAsync(YtdlVersionPath, json.tag_name);
            return;
        }
        if (currentYtdlVersion == latestVersion)
        {
            Log.Information("YT-DLP is up to date.");
        }
        else
        {
            Log.Information("YT-DLP is outdated. Updating...");
            await DownloadYtdl(json);
            await File.WriteAllTextAsync(YtdlVersionPath, json.tag_name);
        }
    }
    
    public static async Task TryDownloadFfmpeg()
    {
        var utilsPath = Path.GetDirectoryName(ConfigManager.Config.ytdlPath);
        if (string.IsNullOrEmpty(utilsPath))
            throw new Exception("Failed to get YT-DLP path");
        if (!ConfigManager.Config.CacheYouTube ||
            File.Exists(Path.Combine(utilsPath, "ffmpeg.exe")))
            return;
        
        Directory.CreateDirectory(utilsPath);
        Log.Information("Downloading FFmpeg...");
        using var response = await HttpClient.GetAsync(FfmpegUrl);
        if (!response.IsSuccessStatusCode)
        {
            Log.Information("Failed to download {Url}: {ResponseStatusCode}", FfmpegUrl, response.StatusCode);
            return;
        }
        
        var filePath = Path.Combine(Program.CurrentProcessPath, Path.GetFileName(FfmpegUrl));
        if (File.Exists(filePath))
            File.Delete(filePath);
        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await response.Content.CopyToAsync(fileStream);
        fileStream.Close();
        
        Log.Information("Extracting FFmpeg zip.");
        ZipFile.ExtractToDirectory(filePath, Program.CurrentProcessPath);
        Log.Information("FFmpeg extracted.");

        var ffmpegPath = Path.Combine(Program.CurrentProcessPath, "ffmpeg-master-latest-win64-gpl");
        var ffmpegBinPath = Path.Combine(ffmpegPath, "bin");
        var ffmpegFiles = Directory.GetFiles(ffmpegBinPath);
        foreach (var ffmpegFile in ffmpegFiles)
        {
            var fileName = Path.GetFileName(ffmpegFile);
            var destPath = Path.Combine(utilsPath, fileName);
            if (File.Exists(destPath))
                File.Delete(destPath);
            File.Move(ffmpegFile, destPath);
        }
        Directory.Delete(ffmpegPath, true);
        File.Delete(filePath);
        Log.Information("FFmpeg downloaded and extracted.");
    }
    
    private static async Task DownloadYtdl(YtApi json)
    {
        foreach (var assetVersion in json.assets)
        {
            if (assetVersion.name != "yt-dlp.exe")
                continue;
            
            await using var stream = await HttpClient.GetStreamAsync(assetVersion.browser_download_url);
            var path = Path.GetDirectoryName(ConfigManager.Config.ytdlPath);
            if (string.IsNullOrEmpty(path))
                throw new Exception("Failed to get YT-DLP path");
            Directory.CreateDirectory(path);
            await using var fileStream = new FileStream(ConfigManager.Config.ytdlPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream);
            Log.Information("Downloaded YT-DLP");
            return;
        }
        throw new Exception("Failed to download YT-DLP");
    }
}