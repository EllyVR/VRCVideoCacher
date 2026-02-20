using System.Diagnostics;
using System.Runtime.InteropServices.Marshalling;
using Serilog;

namespace VRCVideoCacher.Utils;

public class ElevatorManager
{
    private static string ElevatorHash;
    private static string ElevatorPath = Path.Combine("Utils", "VRCVideoCacher.Elevator.exe");
    private static readonly ILogger Log = Program.Logger.ForContext<ElevatorManager>();
    
    public static void Init()
    {
        if (!File.Exists(ElevatorPath))
        {
            WriteFile();
            Log.Information("Elevator written to path.");
        }
        else
        {
            if (GetDiskHash() != GetEmbeddedHash())
            {
                WriteFile();
                Log.Information("Elevator out of date or corrupt. Latest written to disk.");
            }
        }
    }

    public static void AddHostFile()
    {
        Process proc = new Process();
        proc.StartInfo.FileName = ElevatorPath;
        proc.StartInfo.Arguments = "--addhost";
        proc.StartInfo.UseShellExecute = true;
        proc.StartInfo.Verb = "runas";
        proc.Start();

    }
    public static void RemoveHostFile()
    {
        Process proc = new Process();
        proc.StartInfo.FileName = ElevatorPath;
        proc.StartInfo.Arguments = "--removehost";
        proc.StartInfo.UseShellExecute = true;
        proc.StartInfo.Verb = "runas";
        proc.Start();

    }
    private static void WriteFile()
    {
        var stream = GetElevatorStream();
        using var fileStream = File.Create(ElevatorPath);
        stream.CopyTo(fileStream);
        fileStream.Close();
    }

    private static string GetDiskHash()
    {
        var bytes = File.ReadAllBytes(ElevatorPath);
        return Program.ComputeBinaryContentHash(bytes);
    }
    
    private static string GetEmbeddedHash()
    {
        var stream = GetElevatorStream();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        stream.Dispose();
        return Program.ComputeBinaryContentHash(ms.ToArray());
    }
    
    private static Stream GetElevatorStream()
    {
        return Program.GetEmbeddedResource("VRCVideoCacher.VRCVideoCacher.Elevator.exe");
    }
}