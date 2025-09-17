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
    public static readonly string CookiesPath;
    public static readonly string YtdlPath;
    private static readonly string YtdlVersionPath;
    private const string YtdlpApiUrl = "https://api.github.com/repos/yt-dlp/yt-dlp-nightly-builds/releases/latest";
    private const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    static YtdlManager()
    {
        string dataPath;
        if (OperatingSystem.IsWindows())
            dataPath = Program.CurrentProcessPath;
        else
            dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCVideoCacher");

        CookiesPath = Path.Combine(dataPath, "youtube_cookies.txt");
        if (string.IsNullOrEmpty(ConfigManager.Config.ytdlPath))
        {
            if (OperatingSystem.IsWindows())
                YtdlPath = Path.Combine(dataPath, "Utils\\yt-dlp.exe");
            else
                YtdlPath = FileTools.LocateFile("yt-dlp") ?? throw new FileNotFoundException("Unable to find yt-dlp");
        }
        else
        {
            YtdlPath = ConfigManager.Config.ytdlPath;
        }

        YtdlVersionPath = Path.Combine(dataPath, "yt-dlp.version.txt");
        Log.Debug("Using ytdl path: {YtdlPath}", YtdlPath);
    }
    
    public static void StartYtdlDownloadThread()
    {
        Task.Run(YtdlDownloadTask);
    }

    private static async Task YtdlDownloadTask()
    {
        const int interval = 60 * 60 * 1000; // 1 hour
        while (true)
        {
            await Task.Delay(interval);
            await TryDownloadYtdlp();
        }
        // ReSharper disable once FunctionNeverReturns
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
        Log.Information("YT-DLP Current: {Installed} Latest: {Latest}", currentYtdlVersion, latestVersion);
        if (string.IsNullOrEmpty(latestVersion))
        {
            Log.Warning("Failed to check for YT-DLP updates.");
            return;
        }
        if (!File.Exists(YtdlPath))
        {
            Log.Information("YT-DLP is not installed. Downloading...");
            if (await DownloadYtdl(json))
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
            if (await DownloadYtdl(json))
                await File.WriteAllTextAsync(YtdlVersionPath, json.tag_name);
        }
    }

    public static async Task TryDownloadFfmpeg()
    {
        var utilsPath = Path.GetDirectoryName(YtdlPath);
        if (string.IsNullOrEmpty(utilsPath))
            throw new Exception("Failed to get YT-DLP path");

        // Make sure we can write into the folder
        try
        {
            File.Create(Path.Combine(utilsPath, "_temp_permission_prober"), 0, FileOptions.DeleteOnClose);
        }
        catch (Exception ex)
        {
            Log.Warning($"Skipping ffmpeg download: {ex.GetType().Name}: {ex.Message}");
            return;
        }

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
    
    private static async Task<bool> DownloadYtdl(YtApi json)
    {
        if (File.Exists(YtdlPath) && (File.GetAttributes(YtdlPath) & FileAttributes.ReadOnly) != 0)
        {
            Log.Warning("Skipping yt-dlp download because location is unwritable.");
            return false;
        }

        foreach (var assetVersion in json.assets)
        {
            if (assetVersion.name != "yt-dlp.exe")
                continue;

            await using var stream = await HttpClient.GetStreamAsync(assetVersion.browser_download_url);
            var path = Path.GetDirectoryName(YtdlPath);
            if (string.IsNullOrEmpty(path))
                throw new Exception("Failed to get YT-DLP path");
            Directory.CreateDirectory(path);
            await using var fileStream = new FileStream(YtdlPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream);
            Log.Information("Downloaded YT-DLP.");
            return true;
        }
        throw new Exception("Failed to download YT-DLP");
    }
    
    private static readonly List<string> YtdlConfigPaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "yt-dlp.conf"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "yt-dlp", "config"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "yt-dlp", "config.txt")
    ];
    
    public static bool GlobalYtdlConfigExists()
    {
        return YtdlConfigPaths.Any(File.Exists);
    }
    
    public static void DeleteGlobalYtdlConfig()
    {
        foreach (var configPath in YtdlConfigPaths)
        {
            if (File.Exists(configPath))
            {
                Log.Information("Deleting global YT-DLP config: {ConfigPath}", configPath);
                File.Delete(configPath);
            }
        }
    }
}