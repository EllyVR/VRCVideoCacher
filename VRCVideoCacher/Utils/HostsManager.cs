namespace VRCVideoCacher.Elevator;

public class HostsManager
{
    private static readonly Serilog.ILogger Log = Program.Logger.ForContext<HostsManager>();

    private static readonly string Header = $"{Environment.NewLine}# ----- BEGIN VRCVIDEOCACHER -----{Environment.NewLine}";
    private static readonly string Footer = $"{Environment.NewLine}# ----- END VRCVIDEOCACHER -----{Environment.NewLine}";
    private static readonly string HostsPath = OperatingSystem.IsWindows()
        ? $"{Environment.GetFolderPath(Environment.SpecialFolder.System)}/drivers/etc/hosts"
        : "/etc/hosts";
    
    public static void TryRun()
    {
        if (Environment.CommandLine.Contains("--addhost"))
        {
            try
            {
                Add();
                Log.Information("Host entry added successfully.");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to add host entry: " + ex.Message);
                Environment.Exit(1);
            }
        }
        if (Environment.CommandLine.Contains("--removehost"))
        {
            try
            {
                Remove();
                Log.Information("Host entry removed successfully.");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to remove host entry: " + ex.Message);
                Environment.Exit(1);
            }
        }
    }

    private static void Add()
    {
        var hostsFile = File.ReadAllText(HostsPath);
        if (hostsFile.Contains(Header))
            return;
        
        File.AppendAllText(HostsPath,
            $"{Header}127.0.0.1 localhost.youtube.com{Footer}");
    }

    private static void Remove()
    {
        var hostsFile = File.ReadAllText(HostsPath);
        if (!hostsFile.Contains(Header))
            return;

        var headerStart = hostsFile.IndexOf(Header, StringComparison.Ordinal);
        var headerEnd = hostsFile.IndexOf(Footer, StringComparison.Ordinal) + Footer.Length;
        var newHostsFile = hostsFile.Remove(headerStart, headerEnd - headerStart);
        File.WriteAllText(HostsPath, newHostsFile);
    }
    
    public static bool IsHostAdded()
    {
        var hostsFile = File.ReadAllText(HostsPath);
        return hostsFile.Contains(Header);
    }
}