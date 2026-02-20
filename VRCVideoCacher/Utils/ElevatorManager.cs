using System.Diagnostics;
using System.Runtime.InteropServices.Marshalling;
using Serilog;

namespace VRCVideoCacher.Utils;

public class ElevatorManager
{
    private static readonly ILogger Log = Program.Logger.ForContext<ElevatorManager>();

    public static void AddHostFile()
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
        if (proc.ExitCode != 0)
        {
            Log.Error("Failed to add host to file, exit code: {ExitCode}", proc.ExitCode);
        }
    }

    public static void RemoveHostFile()
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
        if (proc.ExitCode != 0)
        {
            Log.Error("Failed to remove host to file, exit code: {ExitCode}", proc.ExitCode);
        }
    }
}