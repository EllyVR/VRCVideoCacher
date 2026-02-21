using System.Diagnostics;
using Serilog;
using VRCVideoCacher.API;
using VRCVideoCacher.Elevator;

namespace VRCVideoCacher.Utils;

public class ElevatorManager
{
    private static readonly ILogger Log = Program.Logger.ForContext<ElevatorManager>();
    private static bool _hasHostsLine;

    public ElevatorManager()
    {
        _hasHostsLine = HostsManager.IsHostAdded();
    }

    public static bool ToggleHostLine()
    {
        if (_hasHostsLine)
            RemoveHostFile();
        else
            AddHostFile();
        
        return _hasHostsLine;
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
        proc.Start();
        proc.WaitForExit();
        if (proc.ExitCode == 0)
        {
            Log.Information("Host entry added successfully.");
            _hasHostsLine = true;
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
        proc.Start();
        proc.WaitForExit();
        if (proc.ExitCode == 0)
        {
            Log.Information("Host entry removed successfully.");
            _hasHostsLine = false;
            ConfigManager.Config.YtdlpWebServerUrl = "http://localhost:9696";
            ConfigManager.TrySaveConfig();
            WebServer.Init();
            return;
        }
        Log.Error("Failed to remove host to file, exit code: {ExitCode}", proc.ExitCode);
    }
}