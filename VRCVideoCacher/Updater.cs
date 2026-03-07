using System.Diagnostics;
using System.Security.Cryptography;
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
    private static readonly string FileName = OperatingSystem.IsWindows() ? "VRCVideoCacher.exe" : "VRCVideoCacher";
    private static readonly string FilePath = Path.Join(Program.CurrentProcessPath, FileName);
    private static readonly string BackupFilePath = Path.Join(Program.CurrentProcessPath, "VRCVideoCacher.bkp");
    private static readonly string TempFilePath = Path.Join(Program.CurrentProcessPath, "VRCVideoCacher.Temp");

    public static async Task CheckForUpdates()
    {
#if STEAMRELEASE
        return;
#endif
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
        using var response = await HttpClient.GetAsync(UpdateUrl);
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
        if (ConfigManager.Config.AutoUpdateVrcVideoCacher)
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
        {
            File.Delete(BackupFilePath);
        }
        var batPath = Path.Join(Program.CurrentProcessPath, "update.bat");
        if (File.Exists(batPath))
        {
            try { File.Delete(batPath); } catch { /* Ignore if still locked */ }
        }
    }

    private static async Task UpdateAsync(GitHubRelease release)
    {
        foreach (var asset in release.assets)
        {
            if (asset.name != FileName)
                continue;

            try
            {
                await using var stream = await HttpClient.GetStreamAsync(asset.browser_download_url);
                await using var fileStream = new FileStream(TempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream);
                fileStream.Close();

                if (await HashCheck(asset.digest))
                {
                    Log.Information("Hash check passed, Replacing binary.");
                    
                    if (!OperatingSystem.IsWindows())
                    {
                        File.Move(FilePath, BackupFilePath);
                        File.Move(TempFilePath, FilePath);
                        FileTools.MarkFileExecutable(FilePath);
                        
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
                    else
                    {
                        // On Windows, running executable cannot be safely renamed while Avalonia is looking for Trimmed UI components.
                        // We use a temporary batch script to swap the binary *after* the current process has fully exited.
                        var batPath = Path.Join(Program.CurrentProcessPath, "update.bat");
                        var batScript = $"""
@echo off
timeout /t 1 /nobreak > NUL
:loop
move /y "{FilePath}" "{BackupFilePath}" > NUL 2>&1
if exist "{FilePath}" goto loop
move /y "{TempFilePath}" "{FilePath}" > NUL
start "" "{FilePath}"
del "%~f0"
""";
                        await File.WriteAllTextAsync(batPath, batScript);
                        
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = batPath,
                                UseShellExecute = true,
                                CreateNoWindow = true,
                                WindowStyle = ProcessWindowStyle.Hidden,
                                WorkingDirectory = Program.CurrentProcessPath
                            }
                        };
                        process.Start();
                        Environment.Exit(0);
                    }
                }
                else
                {
                    Log.Information("Hash check failed, Reverting update.");
                    if (File.Exists(TempFilePath)) File.Delete(TempFilePath);
                    return;
                }
                Log.Information("Updated to version {Version}", release.tag_name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update: {Message}", ex.Message);
                if (File.Exists(TempFilePath))
                {
                    try { File.Delete(TempFilePath); } catch { /* Ignored */ }
                }
            }
        }
    }

    private static async Task<bool> HashCheck(string? githubHash)
    {
        if (string.IsNullOrEmpty(githubHash))
        {
            Log.Warning("No hash provided by GitHub, skipping hash check.");
            return true;
        }

        using var sha256 = SHA256.Create();
        await using var stream = File.Open(TempFilePath, FileMode.Open);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        var hashString = Convert.ToHexString(hashBytes);
        if (githubHash.Contains(':'))
            githubHash = githubHash.Split(':')[1];
            
        var hashMatches = string.Equals(githubHash, hashString, StringComparison.OrdinalIgnoreCase);
        Log.Information("FileHash: {FileHash} GitHubHash: {GitHubHash} HashMatch: {HashMatches}", hashString, githubHash, hashMatches);
        return hashMatches;
    }
}