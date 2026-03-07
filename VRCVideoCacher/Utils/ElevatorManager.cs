using System.Diagnostics;
using Serilog;
using VRCVideoCacher.API;
using VRCVideoCacher.Elevator;

namespace VRCVideoCacher.Utils;

public class ElevatorManager
{
    private static readonly ILogger Log = Program.Logger.ForContext<ElevatorManager>();
    public static bool HasHostsLine = HostsManager.IsHostAdded();

    public static void ToggleHostLine()
    {
        if (HasHostsLine)
            RemoveHostFile();
        else
            AddHostFile();
    }

    private static void AddHostFile()
    {
        var proc = new Process
        {
            StartInfo =
            {
                FileName = Environment.ProcessPath,
                Arguments = "--addhost",
                UseShellExecute = true,
                Verb = "runas"
            }
        };
        try
        {
            proc.Start();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log.Warning("User cancelled UAC prompt for adding host entry.");
            return;
        }
        proc.WaitForExit();
        if (proc.ExitCode == 0)
        {
            Log.Information("Host entry added successfully.");
            HasHostsLine = true;
            ConfigManager.Config.YtdlpWebServerUrl = "http://localhost.youtube.com:9696";
            ConfigManager.TrySaveConfig();
            WebServer.Init();
            return;
        }
        Log.Error("Failed to add host to file, exit code: {ExitCode}", proc.ExitCode);
    }

    private static void RemoveHostFile()
    {
        var proc = new Process
        {
            StartInfo =
            {
                FileName = Environment.ProcessPath,
                Arguments = "--removehost",
                UseShellExecute = true,
                Verb = "runas"
            }
        };
        try
        {
            proc.Start();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log.Warning("User cancelled UAC prompt for removing host entry.");
            return;
        }
        proc.WaitForExit();
        if (proc.ExitCode == 0)
        {
            Log.Information("Host entry removed successfully.");
            HasHostsLine = false;
            ConfigManager.Config.YtdlpWebServerUrl = "http://localhost:9696";
            ConfigManager.TrySaveConfig();
            WebServer.Init();
            return;
        }
        Log.Error("Failed to remove host to file, exit code: {ExitCode}", proc.ExitCode);
    }
}