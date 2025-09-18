using Newtonsoft.Json;
using System.Runtime.InteropServices;
using Serilog;
using SharpCompress.Readers;
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
    private const string FfmpegApiUrl = "https://api.github.com/repos/yt-dlp/FFmpeg-Builds/releases/latest";

    static YtdlManager()
    {
        string dataPath;
        if (OperatingSystem.IsWindows())
            dataPath = Program.CurrentProcessPath;
        else
            dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCVideoCacher");

        CookiesPath = Path.Combine(dataPath, "youtube_cookies.txt");

        if (string.IsNullOrEmpty(ConfigManager.Config.ytdlPath))
            YtdlPath = FileTools.LocateFile(OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp") ?? throw new FileNotFoundException("Unable to find yt-dlp");
        else
            YtdlPath = ConfigManager.Config.ytdlPath;

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
        var json = JsonConvert.DeserializeObject<GitHubRelease>(data);
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
            var probeFilePath = Path.Combine(utilsPath, "_temp_permission_prober");
            if (File.Exists(probeFilePath))
                File.Delete(probeFilePath);
            File.Create(probeFilePath, 0, FileOptions.DeleteOnClose);
        }
        catch (Exception ex)
        {
            Log.Warning($"Skipping ffmpeg download: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (!ConfigManager.Config.CacheYouTube ||
            File.Exists(Path.Combine(utilsPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg")))
            return;

        var apiResponse = await HttpClient.GetAsync(FfmpegApiUrl);
        if (!apiResponse.IsSuccessStatusCode)
        {
            Log.Warning("Failed to get latest ffmpeg release: {ResponseStatusCode}", apiResponse.StatusCode);
            return;
        }
        var data = await apiResponse.Content.ReadAsStringAsync();
        var json = JsonConvert.DeserializeObject<GitHubRelease>(data);
        if (json == null)
        {
            Log.Error("Failed to parse ffmpeg release response.");
            return;
        }

        string assetName;
        if (OperatingSystem.IsWindows())
        {
            assetName = "ffmpeg-master-latest-win64-gpl.zip";
        }
        else if (OperatingSystem.IsLinux())
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64:
                    assetName = "ffmpeg-master-latest-linux64-gpl.tar.xz";
                    break;
                case Architecture.Arm64:
                    assetName = "ffmpeg-master-latest-linuxarm64-gpl.tar.xz";
                    break;
                default:
                    Log.Error("Unsupported architecture {OSArchitecture}", RuntimeInformation.OSArchitecture);
                    return;
            }
        }
        else
        {
            Log.Error("Unsupported operating system {OperatingSystem}", Environment.OSVersion);
            return;
        }
        // ffmpeg-master-latest-linux64-gpl.tar.xz -> ffmpeg-master-latest-linux64-gpl
        var folderName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(assetName));

        var assets = json.assets.Where(asset => asset.name == assetName).ToList();
        if (assets.Count < 1)
        {
            Log.Error("Unable to find ffmpeg asset {AssetName} for this platform.", assetName);
            return;
        }

        Log.Information("Downloading FFmpeg...");
        var url = assets.First().browser_download_url;

        using var response = await HttpClient.GetAsync(url);
        using var responseStream = await response.Content.ReadAsStreamAsync();
        using (var reader = ReaderFactory.Open(responseStream))
        {
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.Key == null || reader.Entry.IsDirectory)
                    continue;

                var nameStripped = reader.Entry.Key.Replace($"{folderName}/bin/", string.Empty);
                if (nameStripped != reader.Entry.Key)
                {
                    Log.Debug("Extracting file {Name} ({Size} bytes)", nameStripped, reader.Entry.Size);
                    var path = Path.Combine(utilsPath, nameStripped);
                    await using var outputStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                    using var entryStream = reader.OpenEntryStream();
                    await entryStream.CopyToAsync(outputStream);
                    FileTools.MarkFileExecutable(path);
                }
            }
        }

        Log.Information("FFmpeg downloaded and extracted.");
    }
    
    private static async Task<bool> DownloadYtdl(GitHubRelease json)
    {
        if (File.Exists(YtdlPath) && (File.GetAttributes(YtdlPath) & FileAttributes.ReadOnly) != 0)
        {
            Log.Warning("Skipping yt-dlp download because location is unwritable.");
            return false;
        }

        string assetName;
        if (OperatingSystem.IsWindows())
        {
            assetName = "yt-dlp.exe";
        }
        else if (OperatingSystem.IsLinux())
        {
            assetName = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "yt-dlp_linux",
                Architecture.Arm64 => "yt-dlp_linux_aarch64",
                _ => throw new Exception($"Unsupported architecture {RuntimeInformation.OSArchitecture}"),
            };
        }
        else
        {
            throw new Exception($"Unsupported operating system {Environment.OSVersion}");
        }

        foreach (var assetVersion in json.assets)
        {
            if (assetVersion.name != assetName)
                continue;

            await using var stream = await HttpClient.GetStreamAsync(assetVersion.browser_download_url);
            var path = Path.GetDirectoryName(YtdlPath);
            if (string.IsNullOrEmpty(path))
                throw new Exception("Failed to get YT-DLP path");
            Directory.CreateDirectory(path);
            await using var fileStream = new FileStream(YtdlPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream);
            Log.Information("Downloaded YT-DLP.");
            FileTools.MarkFileExecutable(YtdlPath);
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