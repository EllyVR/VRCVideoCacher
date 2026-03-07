using System.Diagnostics;
using Serilog;
using VRCVideoCacher.API;
using VRCVideoCacher.Elevator;

namespace VRCVideoCacher.Utils;

public class ElevatorManager
{
    private static readonly ILogger Log = Program.Logger.ForContext<ElevatorManager>();
    public static bool HasHostsLine = HostsManager.IsHostAdded();

    private static readonly bool InPressureVessel = Directory.Exists("/run/pressure-vessel");

    private static string? FindLaunchClient()
    {
        string[] candidates =
        [
            "/usr/lib/pressure-vessel/from-host/bin/steam-runtime-launch-client",
            "/usr/bin/steam-runtime-launch-client",
            "/usr/lib/pressure-vessel/bin/steam-runtime-launch-client",
        ];
        foreach (var path in candidates)
            if (File.Exists(path)) return path;
        return null;
    }

    private static string? FindHostBin(string name)
    {
        var paths = new[] { $"/usr/bin/{name}", $"/bin/{name}", $"/usr/local/bin/{name}" };
        foreach (var p in paths)
        {
            var check = InPressureVessel ? $"/run/host{p}" : p;
            if (File.Exists(check)) return p;
        }
        return null;
    }

    private static Process? MakeLinuxElevatedProcess(string flag)
    {
        var launchClient = InPressureVessel ? FindLaunchClient() : null;
        Log.Debug("InPressureVessel={InPressureVessel} launch-client={LC}", InPressureVessel, launchClient ?? "n/a");

        string appPath = Environment.ProcessPath!;

        ProcessStartInfo MakeStartInfo(string exe, string args) => launchClient != null
            ? new ProcessStartInfo { FileName = launchClient, Arguments = $"--alongside-steam -- {exe} {args}", UseShellExecute = false }
            : new ProcessStartInfo { FileName = exe, Arguments = args, UseShellExecute = false };

        // 1. Try pkexec
        var pkexec = FindHostBin("pkexec");
        if (pkexec != null)
        {
            Log.Debug("Using pkexec");
            return new Process { StartInfo = MakeStartInfo(pkexec, $"{appPath} {flag}") };
        }

        // 2. Try sudo -A with a graphical askpass helper
        string[] askpassCandidates =
        [
            "/usr/lib/openssh/gnome-ssh-askpass",
            "/usr/lib/ssh/x11-ssh-askpass",
            "/usr/bin/ksshaskpass",
            "/usr/lib/seahorse/seahorse-ssh-askpass",
        ];
        foreach (var askpass in askpassCandidates)
        {
            var check = InPressureVessel ? $"/run/host{askpass}" : askpass;
            if (!File.Exists(check)) continue;
            Log.Debug("Using sudo -A with askpass: {Askpass}", askpass);
            var psi = MakeStartInfo("/usr/bin/sudo", $"-A {appPath} {flag}");
            psi.Environment["SUDO_ASKPASS"] = askpass;
            return new Process { StartInfo = psi };
        }

        // 3. Fall back to a terminal emulator with sudo
        string[] terminals = ["x-terminal-emulator", "xterm", "konsole", "gnome-terminal", "xfce4-terminal", "mate-terminal"];
        foreach (var term in terminals)
        {
            var termPath = FindHostBin(term);
            if (termPath == null) continue;
            Log.Debug("Using terminal {Term} with sudo", termPath);
            var termArgs = termPath.Contains("gnome-terminal")
                ? $"-- /usr/bin/sudo {appPath} {flag}"
                : $"-e /usr/bin/sudo {appPath} {flag}";
            return new Process { StartInfo = MakeStartInfo(termPath, termArgs) };
        }

        Log.Error("No elevation method found. Please manually edit /etc/hosts.");
        return null;
    }

    public static void ToggleHostLine()
    {
        if (HasHostsLine)
            RemoveHostFile();
        else
            AddHostFile();
    }

    private static void AddHostFile()
    {
        Process? proc;
        if (OperatingSystem.IsWindows())
        {
            proc = new Process { StartInfo = { FileName = Environment.ProcessPath, Arguments = "--addhost", UseShellExecute = true, Verb = "runas" } };
            try { proc.Start(); }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Log.Warning("User cancelled UAC prompt.");
                return;
            }
        }
        else
        {
            proc = MakeLinuxElevatedProcess("--addhost");
            if (proc == null) return;
            try { proc.Start(); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to launch privilege elevator for adding host entry.");
                return;
            }
        }

        proc.WaitForExit();

        if (HostsManager.IsHostAdded())
        {
            Log.Information("Host entry added successfully.");
            HasHostsLine = true;
            ConfigManager.Config.YtdlpWebServerUrl = "http://localhost.youtube.com:9696";
            ConfigManager.TrySaveConfig();
            WebServer.Init();
        }
        else
        {
            Log.Warning("Host entry not found after elevation — user may have cancelled or elevation failed (exit code: {ExitCode}).", proc.ExitCode);
        }
    }

    private static void RemoveHostFile()
    {
        Process? proc;
        if (OperatingSystem.IsWindows())
        {
            proc = new Process { StartInfo = { FileName = Environment.ProcessPath, Arguments = "--removehost", UseShellExecute = true, Verb = "runas" } };
            try { proc.Start(); }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Log.Warning("User cancelled UAC prompt.");
                return;
            }
        }
        else
        {
            proc = MakeLinuxElevatedProcess("--removehost");
            if (proc == null) return;
            try { proc.Start(); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to launch privilege elevator for removing host entry.");
                return;
            }
        }

        proc.WaitForExit();

        if (!HostsManager.IsHostAdded())
        {
            Log.Information("Host entry removed successfully.");
            HasHostsLine = false;
            ConfigManager.Config.YtdlpWebServerUrl = "http://localhost:9696";
            ConfigManager.TrySaveConfig();
            WebServer.Init();
        }
        else
        {
            Log.Warning("Host entry still present after elevation — user may have cancelled or elevation failed (exit code: {ExitCode}).", proc.ExitCode);
        }
    }
}
