using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.Utils;

public class Uninstall
{
    private static readonly ILogger Log = Program.Logger.ForContext<Uninstall>();

    public static void StartDetached()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Environment.ProcessPath,
            Arguments = "--uninstall",
            UseShellExecute = false
        });
    }

    public static void Run()
    {
        Log.Information("Uninstalling VRCVideoCacher...");

        WaitForOtherInstances();

        if (OperatingSystem.IsWindows())
            TryRun("remove VRCX startup shortcut", AutoStartShortcut.RemoveShortcuts);

        TryRun("remove hosts entry", RemoveHostsEntry);
        TryRun("delete cached assets", DeleteDefaultCachedAssets);
        TryRun("unregister from SteamVR", UnregisterFromSteamVr);

        Log.Information("Uninstall complete.");
    }

    private static void TryRun(string description, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to {Description}", description);
        }
    }

    private static void WaitForOtherInstances()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                if (process.Id == current.Id)
                    continue;

                if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
                    Log.Warning("VRCVideoCacher (PID {Pid}) is still running", process.Id);
            }
        }
    }

    private static void RemoveHostsEntry()
    {
        if (!HostsManager.IsHostAdded())
            return;

        try
        {
            HostsManager.Remove();
        }
        catch (UnauthorizedAccessException)
        {
            Log.Information("Requesting elevation to remove hosts entry...");
            ElevatorManager.StartElevated("--removehost");
        }

        if (HostsManager.IsHostAdded())
            Log.Warning("Hosts entry is still present");
        else
            Log.Information("Removed hosts entry");
    }

    private static void UnregisterFromSteamVr()
    {
        if (!OpenVRService.TryRemoveManifest())
            RemoveManifestFromAppConfig();

        if (File.Exists(OpenVRService.ManifestPath))
            File.Delete(OpenVRService.ManifestPath);
    }

    private static void RemoveManifestFromAppConfig()
    {
        var appConfigPath = GetSteamAppConfigPath();
        if (appConfigPath == null)
        {
            Log.Information("SteamVR appconfig.json not found, nothing to unregister");
            return;
        }

        var appConfig = JObject.Parse(File.ReadAllText(appConfigPath));
        if (appConfig["manifest_paths"] is not JArray manifestPaths)
            return;

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var manifestPath = Path.GetFullPath(OpenVRService.ManifestPath);
        var ours = manifestPaths
            .Where(p => string.Equals(Path.GetFullPath(p.ToString()), manifestPath, comparison))
            .ToList();
        if (ours.Count == 0)
        {
            Log.Information("Not registered with SteamVR");
            return;
        }

        foreach (var path in ours)
            path.Remove();

        File.WriteAllText(appConfigPath, appConfig.ToString(Formatting.Indented));
        Log.Information("Removed SteamVR manifest from {AppConfigPath}", appConfigPath);
    }

    private static string? GetSteamAppConfigPath()
    {
        return FileTools.GetSteamInstallPaths()
            .Select(path => Path.Join(path, "config", "appconfig.json"))
            .FirstOrDefault(File.Exists);
    }

    private static void DeleteDefaultCachedAssets()
    {
        var cachePath = Program.DefaultCachePath;
        if (!Directory.Exists(cachePath))
            return;

        Directory.Delete(cachePath, true);
        Log.Information("Deleted cached assets at {CachePath}", cachePath);
    }
}
