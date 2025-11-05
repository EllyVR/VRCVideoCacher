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
    private const string YtdlpApiUrl = "https://api.github.com/repos/yt-dlp/yt-dlp-nightly-builds/releases/latest";
    private const string FfmpegNightlyApiUrl = "https://api.github.com/repos/yt-dlp/FFmpeg-Builds/releases/latest";
    private const string FfmpegApiUrl = "https://api.github.com/repos/GyanD/codexffmpeg/releases/latest";
    private const string DenoApiUrl = "https://api.github.com/repos/denoland/deno/releases/latest";

    static YtdlManager()
    {
        CookiesPath = Path.Combine(Program.DataPath, "youtube_cookies.txt");

        // try to locate in PATH
        if (string.IsNullOrEmpty(ConfigManager.Config.ytdlPath))
            YtdlPath = FileTools.LocateFile(OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp") ?? throw new FileNotFoundException("Unable to find yt-dlp");
        else if (Path.IsPathRooted(ConfigManager.Config.ytdlPath))
            YtdlPath = ConfigManager.Config.ytdlPath;
        else
            YtdlPath = Path.Combine(Program.DataPath, ConfigManager.Config.ytdlPath);
        
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

        var currentYtdlVersion = Versions.CurrentVersion.ytdlp;
        if (!File.Exists(YtdlPath))
            currentYtdlVersion = "Not Installed";

        var latestVersion = json.tag_name;
        Log.Information("YT-DLP Current: {Installed} Latest: {Latest}", currentYtdlVersion, latestVersion);
        if (string.IsNullOrEmpty(latestVersion))
        {
            Log.Warning("Failed to check for YT-DLP updates.");
            return;
        }
        if (currentYtdlVersion == latestVersion)
        {
            Log.Information("YT-DLP is up to date.");
            return;
        }
        Log.Information("YT-DLP is outdated. Updating...");

        await DownloadYtdl(json);
    }

    public static async Task TryDownloadDeno()
    {
        if (string.IsNullOrEmpty(ConfigManager.UtilsPath))
            throw new Exception("Failed to get Utils path");
        
        var denoPath = Path.Combine(ConfigManager.UtilsPath, OperatingSystem.IsWindows() ? "deno.exe" : "deno");
        
        var apiResponse = await HttpClient.GetAsync(DenoApiUrl);
        if (!apiResponse.IsSuccessStatusCode)
        {
            Log.Warning("Failed to get latest ffmpeg release: {ResponseStatusCode}", apiResponse.StatusCode);
            return;
        }
        var data = await apiResponse.Content.ReadAsStringAsync();
        var json = JsonConvert.DeserializeObject<GitHubRelease>(data);
        if (json == null)
        {
            Log.Error("Failed to parse deno release response.");
            return;
        }
        
        var currentDenoVersion = Versions.CurrentVersion.deno;
        if (!File.Exists(denoPath))
            currentDenoVersion = "Not Installed";

        var latestVersion = json.tag_name;
        Log.Information("Deno Current: {Installed} Latest: {Latest}", currentDenoVersion, latestVersion);
        if (string.IsNullOrEmpty(latestVersion))
        {
            Log.Warning("Failed to check for Deno updates.");
            return;
        }
        if (currentDenoVersion == latestVersion)
        {
            Log.Information("Deno is up to date.");
            return;
        }
        Log.Information("Deno is outdated. Updating...");

        string assetName;
        if (OperatingSystem.IsWindows())
        {
            assetName = "deno-x86_64-pc-windows-msvc.zip";
        }
        else if (OperatingSystem.IsLinux())
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64:
                    assetName = "deno-x86_64-unknown-linux-gnu.zip";
                    break;
                case Architecture.Arm64:
                    assetName = "deno-aarch64-unknown-linux-gnu.zip";
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
        // deno-x86_64-pc-windows-msvc.zip -> deno-x86_64-pc-windows-msvc
        var assets = json.assets.Where(asset => asset.name == assetName).ToList();
        if (assets.Count < 1)
        {
            Log.Error("Unable to find Deno asset {AssetName} for this platform.", assetName);
            return;
        }

        Log.Information("Downloading Deno...");
        var url = assets.First().browser_download_url;

        using var response = await HttpClient.GetAsync(url);
        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var reader = ReaderFactory.Open(responseStream);
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.Key == null || reader.Entry.IsDirectory)
                continue;
            
            Log.Debug("Extracting file {Name} ({Size} bytes)", reader.Entry.Key, reader.Entry.Size);
            var path = Path.Combine(ConfigManager.UtilsPath, reader.Entry.Key);
            await using var outputStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var entryStream = reader.OpenEntryStream();
            await entryStream.CopyToAsync(outputStream);
            FileTools.MarkFileExecutable(path);
            Versions.CurrentVersion.deno = json.tag_name;
            Versions.Save();
            Log.Information("Deno downloaded and extracted.");
            return;
        }

        Log.Error("Failed to extract Deno files.");
    }

    public static async Task TryDownloadFfmpeg()
    {
        if (string.IsNullOrEmpty(ConfigManager.UtilsPath))
            throw new Exception("Failed to get Utils path");

        var ffmpegPath = Path.Combine(ConfigManager.UtilsPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");

        // Make sure we can write into the folder
        try
        {
            var probeFilePath = Path.Combine(ConfigManager.UtilsPath, "_temp_permission_prober");
            if (File.Exists(probeFilePath))
                File.Delete(probeFilePath);
            File.Create(probeFilePath, 0, FileOptions.DeleteOnClose);
        }
        catch (Exception ex)
        {
            Log.Warning($"Skipping ffmpeg download: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (!ConfigManager.Config.CacheYouTube)
            return;

        var apiResponse = await HttpClient.GetAsync(OperatingSystem.IsWindows() ? FfmpegApiUrl : FfmpegNightlyApiUrl);
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

        var currentffmpegVersion = Versions.CurrentVersion.ffmpeg;
        if (!File.Exists(ffmpegPath))
            currentffmpegVersion = "Not Installed";

        var latestVersion = OperatingSystem.IsWindows() ? json.tag_name : json.name;
        Log.Information("FFmpeg Current: {Installed} Latest: {Latest}", currentffmpegVersion, latestVersion);
        if (string.IsNullOrEmpty(latestVersion))
        {
            Log.Warning("Failed to check for FFmpeg updates.");
            return;
        }
        if (currentffmpegVersion == latestVersion)
        {
            Log.Information("FFmpeg is up to date.");
            return;
        }
        Log.Information("FFmpeg is outdated. Updating...");

        string assetSuffix;
        if (OperatingSystem.IsWindows())
        {
            assetSuffix = "full_build-shared.zip";
        }
        else if (OperatingSystem.IsLinux())
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64:
                    assetSuffix = "master-latest-linux64-gpl.tar.xz";
                    break;
                case Architecture.Arm64:
                    assetSuffix = "master-latest-linuxarm64-gpl.tar.xz";
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
        var url = json.assets
            .FirstOrDefault(assetVersion => assetVersion.name.EndsWith(assetSuffix, StringComparison.OrdinalIgnoreCase))
            ?.browser_download_url ?? string.Empty;
        if (string.IsNullOrEmpty(url))
        {
            Log.Error("Unable to find ffmpeg asset for this platform.");
            return;
        }
        Log.Information("Downloading FFmpeg...");

        using var response = await HttpClient.GetAsync(url);
        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var reader = ReaderFactory.Open(responseStream);
        var success = false;
        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.Key == null || reader.Entry.IsDirectory)
                continue;

            if (reader.Entry.Key.Contains("/bin/"))
            {
                var fileName = Path.GetFileName(reader.Entry.Key);
                Log.Debug("Extracting file {Name} ({Size} bytes)", fileName, reader.Entry.Size);
                var path = Path.Combine(ConfigManager.UtilsPath, fileName);
                await using var outputStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                await using var entryStream = reader.OpenEntryStream();
                await entryStream.CopyToAsync(outputStream);
                FileTools.MarkFileExecutable(path);
                success = true;
            }
        }

        if (!success)
        {
            Log.Error("Failed to extract ffmpeg files.");
            return;
        }
        
        Versions.CurrentVersion.ffmpeg = latestVersion;
        Versions.Save();
        Log.Information("FFmpeg downloaded and extracted.");
    }
    
    private static async Task DownloadYtdl(GitHubRelease json)
    {
        if (File.Exists(YtdlPath) && File.GetAttributes(YtdlPath).HasFlag(FileAttributes.ReadOnly))
        {
            Log.Warning("Skipping yt-dlp download because location is unwritable.");
            return;
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
            if (string.IsNullOrEmpty(ConfigManager.UtilsPath))
                throw new Exception("Failed to get YT-DLP path");

            await using var fileStream = new FileStream(YtdlPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream);
            Log.Information("Downloaded YT-DLP.");
            FileTools.MarkFileExecutable(YtdlPath);
            Versions.CurrentVersion.ytdlp = json.tag_name;
            Versions.Save();
            return;
        }
        throw new Exception("Failed to download YT-DLP");
    }
    
    private static readonly List<string> YtdlConfigPaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "yt-dlp.conf"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "yt-dlp", "config"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "yt-dlp", "config.txt"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "yt-dlp", "config"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "yt-dlp", "config.txt"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "yt-dlp.conf"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "yt-dlp.conf.txt"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "yt-dlp/config"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "yt-dlp/config.txt"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yt-dlp/config"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yt-dlp/config.txt"),
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