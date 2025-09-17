using System.Diagnostics;
using Newtonsoft.Json;
using Semver;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher;

public class Updater
{
    private const string UpdateUrl = "https://api.github.com/repos/EllyVR/VRCVideoCacher/releases/latest";
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher.Updater" } }
    };
    private static readonly ILogger Log = Program.Logger.ForContext<Updater>();
    private const string FileName = "VRCVideoCacher.exe";
    private const string BackupFileName = "VRCVideoCacher.exe.bkp";
    private static readonly string FilePath = Path.Combine(Program.CurrentProcessPath, FileName);
    private static readonly string BackupFilePath = Path.Combine(Program.CurrentProcessPath, BackupFileName);
        
    public static async Task CheckForUpdates()
    {
        Log.Information("Checking for updates...");
        var isDebug = false;
#if DEBUG
            isDebug = true;
#endif
        if (Program.Version.Contains("-dev") || isDebug)
        {
            Log.Information("Running in dev mode. Skipping update check.");
            return;
        }
        var response = await HttpClient.GetAsync(UpdateUrl);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Failed to check for updates.");
            return;
        }
        var data = await response.Content.ReadAsStringAsync();
        var latestRelease = JsonConvert.DeserializeObject<GitHubRelease>(data);
        if (latestRelease == null)
        {
            Log.Error("Failed to parse update response.");
            return;
        }
        var latestVersion = SemVersion.Parse(latestRelease.tag_name);
        var currentVersion = SemVersion.Parse(Program.Version);
        Log.Information("Latest release: {Latest}, Installed Version: {Installed}", latestVersion, currentVersion);
        if (SemVersion.ComparePrecedence(currentVersion, latestVersion) >= 0)
        {
            Log.Information("No updates available.");
            return;
        }
        Log.Information("Update available: {Version}", latestVersion);
        if (ConfigManager.Config.AutoUpdate)
        {
            await UpdateAsync(latestRelease);
            return;
        }
        Log.Information(
            "Auto Update is disabled. Please update manually from the releases page. https://github.com/EllyVR/VRCVideoCacher/releases");
    }
        
    public static void Cleanup()
    {
        if (File.Exists(BackupFilePath))
            File.Delete(BackupFilePath);
    }
        
    private static async Task UpdateAsync(GitHubRelease release)
    {
        foreach (var asset in release.assets)
        {
            if (asset.name != FileName)
                continue;
                
            File.Move(FilePath, BackupFilePath);
            try
            {
                await using var stream = await HttpClient.GetStreamAsync(asset.browser_download_url);
                await using var fileStream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream);
                fileStream.Close();
                Log.Information("Updated to version {Version}", release.tag_name);
                
                var process = new Process()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FilePath,
                        UseShellExecute = true,
                        WorkingDirectory = Program.CurrentProcessPath
                    }
                };
                process.Start();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to update: {Message}", ex.Message);
                File.Move(BackupFilePath, FilePath);
                Console.ReadKey();
            }
        }
    }
}