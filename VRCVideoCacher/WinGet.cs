using System.Diagnostics;
using System.Text;
using Serilog;

namespace VRCVideoCacher;

public class WinGet
{
    private static readonly ILogger Log = Program.Logger.ForContext<WinGet>();
    private const string WingetExe = "winget.exe";
    private static readonly Dictionary<string, string> _wingetPackages = new()
    {
        { "VP9 Video Extensions", "9n4d0msmp0pt" },
        { "AV1 Video Extension", "9mvzqvxjbq9v" },
        { "Dolby Digital Plus decoder for PC OEMs", "9nvjqjbdkn97" }
    };
    
    public static async Task TryInstallPackages()
    {
        if (!IsOurPackagesInstalled())
        {
            Log.Information("Installing missing packages...");
            await InstallAllPackages();
        }
    }

    private static bool IsOurPackagesInstalled()
    {
        var installedPackages = GetInstalledPackages();
        foreach (var package in _wingetPackages.Keys)
        {
            if (!installedPackages.Contains(package))
                return false;
        }

        return true;
    }

    private static List<string> GetInstalledPackages()
    {
        var installedPackages = new List<string>();
        try
        {
            var process = new Process
            {
                StartInfo =
                {
                    FileName = WingetExe,
                    Arguments = "list",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                }
            };
            process.Start();
            while (!process.StandardOutput.EndOfStream)
            {
                var line = process.StandardOutput.ReadLine();
                if (line == null || string.IsNullOrEmpty(line.Trim()))
                    continue;

                var split = line.Split("  ");
                if (split.Length > 1 && !string.IsNullOrEmpty(split[0]))
                    installedPackages.Add(split[0]);
            }

            process.WaitForExit();
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
        }

        return installedPackages;
    }

    private static async Task InstallAllPackages()
    {
        foreach (var package in _wingetPackages.Values)
        {
            await InstallPackage(package);
        }
    }

    private static async Task InstallPackage(string packageId)
    {
        try
        {
            var process = new Process
            {
                StartInfo =
                {
                    FileName = WingetExe,
                    Arguments = $"install --id {packageId} -s msstore --accept-package-agreements --accept-source-agreements",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                }
            };
            process.Start();
            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line != null && !string.IsNullOrEmpty(line.Trim()))
                    Log.Debug("{Winget}: " + line, WingetExe);
            }
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
                throw new Exception($"Installation failed with exit code {process.ExitCode}. Error: {error}");
            
            if (process.ExitCode == 0)
                Log.Information("Successfully installed package: {PackageId}", packageId);
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
        }
    }
}