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
    private static readonly string TempFilePath = Path.Join(Program.CurrentProcessPath, OperatingSystem.IsWindows() ? "VRCVideoCacher.Temp.exe" : "VRCVideoCacher.Temp");

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
            File.Delete(BackupFilePath);
        if (File.Exists(TempFilePath))
        {
            Log.Information("Leftover temp file found, deleting.");
            File.Delete(TempFilePath);
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
                if (File.Exists(TempFilePath))
                {
                    Log.Information("Temp file found from a previous update, deleting.");
                    File.Delete(TempFilePath);
                }

                await using var stream = await HttpClient.GetStreamAsync(asset.browser_download_url);
                await using var fileStream = new FileStream(TempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream);
                fileStream.Close();

                if (!await HashCheck(asset.digest))
                {
                    Log.Information("Hash check failed, aborting update.");
                    File.Delete(TempFilePath);
                    return;
                }

                Log.Information("Hash check passed, launching updater.");

                if (!OperatingSystem.IsWindows())
                    FileTools.MarkFileExecutable(TempFilePath);

                // Build args: --do-update <original-exe> --old-pid <pid> [passthrough-args]
                var pid = Environment.ProcessId;
                var passthroughArgs = Environment.GetCommandLineArgs().Skip(1)
                    .Where(a => !a.Equals("--do-update", StringComparison.OrdinalIgnoreCase));
                var argsList = new List<string>
                {
                    "--do-update", FilePath,
                    "--old-pid", pid.ToString()
                };
                argsList.AddRange(passthroughArgs);

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = TempFilePath,
                        UseShellExecute = true,
                        WorkingDirectory = Program.CurrentProcessPath,
                        Arguments = string.Join(" ", argsList.Select(QuoteArg))
                    }
                };
                process.Start();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to update: {Message}", ex.ToString());
                if (File.Exists(TempFilePath))
                    File.Delete(TempFilePath);
            }
        }
    }

    /// <summary>
    /// Handles the --do-update flow. Called from Program.Main before any other init.
    /// Waits for the old process to exit, copies self to the target path, verifies the
    /// hash, then launches the new binary and exits.
    /// </summary>
    public static void RunUpdateHandler(string[] args)
    {
        string? originalPath = null;
        int? oldPid = null;
        var passthrough = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--do-update", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                originalPath = args[++i];
            }
            else if (args[i].Equals("--old-pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var pid))
                    oldPid = pid;
            }
            else
            {
                passthrough.Add(args[i]);
            }
        }

        if (originalPath == null)
        {
            Console.Error.WriteLine("[Updater] --do-update requires a target path argument.");
            Environment.Exit(1);
            return;
        }

        Console.WriteLine($"[Updater] Waiting for old process (PID {oldPid}) to exit...");
        if (oldPid.HasValue)
        {
            try
            {
                using var oldProcess = Process.GetProcessById(oldPid.Value);
                oldProcess.WaitForExit(10_000);
            }
            catch
            {
                // Process already gone — that's fine
            }
        }
        else
        {
            Thread.Sleep(1500);
        }

        var selfPath = Environment.ProcessPath!;
        Console.WriteLine($"[Updater] Copying {selfPath} → {originalPath}");

        try
        {
            File.Copy(selfPath, originalPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Updater] Copy failed: {ex.Message}");
            Environment.Exit(1);
            return;
        }

        // Verify the copy matches self
        if (!FilesHashMatch(selfPath, originalPath))
        {
            Console.Error.WriteLine("[Updater] Hash mismatch after copy — aborting.");
            Environment.Exit(1);
            return;
        }

        Console.WriteLine("[Updater] Hash verified. Update complete.");

        if (!OperatingSystem.IsWindows())
        {
            FileTools.MarkFileExecutable(originalPath);
            Console.WriteLine("[Updater] Marked as executable.");
        }

        // Launch the newly placed binary
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = originalPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(originalPath),
                Arguments = passthrough.Count > 0 ? string.Join(" ", passthrough.Select(QuoteArg)) : string.Empty
            }
        };
        process.Start();

        // Clean up self (the temp file)
        DeleteSelf(selfPath);

        Environment.Exit(0);
    }

    private static bool FilesHashMatch(string pathA, string pathB)
    {
        using var sha = SHA256.Create();
        using var a = File.OpenRead(pathA);
        using var b = File.OpenRead(pathB);
        var hashA = Convert.ToHexString(sha.ComputeHash(a));
        sha.Initialize();
        var hashB = Convert.ToHexString(sha.ComputeHash(b));
        var match = string.Equals(hashA, hashB, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"[Updater] Hash self={hashA} copy={hashB} match={match}");
        return match;
    }

    private static void DeleteSelf(string selfPath)
    {
        for (var i = 0; i < 3; i++)
        {
            try
            {
                File.Delete(selfPath);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(500);
            }
        }
        // Non-critical — leftover temp file is cleaned up by Cleanup() on next run
    }

    private static string QuoteArg(string arg) =>
        arg.Contains(' ') ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg;

    private static async Task<bool> HashCheck(string githubHash)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.Open(TempFilePath, FileMode.Open);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        var hashString = Convert.ToHexString(hashBytes);
        githubHash = githubHash.Split(':')[1];
        var hashMatches = string.Equals(githubHash, hashString, StringComparison.OrdinalIgnoreCase);
        Log.Information("FileHash: {FileHash} GitHubHash: {GitHubHash} HashMatch: {HashMatches}", hashString, githubHash, hashMatches);
        return hashMatches;
    }
}