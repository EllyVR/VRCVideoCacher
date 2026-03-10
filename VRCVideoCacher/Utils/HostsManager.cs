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
                Log.Error(ex, "Failed to add host entry: " + ex.ToString());
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
                Log.Error(ex, "Failed to remove host entry: " + ex.ToString());
                Environment.Exit(1);
            }
        }
    }

    private static void Add()
    {
        CreateHostsIfNotExists();
        var hostsFile = File.ReadAllText(HostsPath);
        if (hostsFile.Contains(Header))
            return;
        
        File.AppendAllText(HostsPath,
            $"{Header}127.0.0.1 localhost.youtube.com{Footer}");
    }

    private static void Remove()
    {
        CreateHostsIfNotExists();
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
        CreateHostsIfNotExists();
        var hostsFile = File.ReadAllText(HostsPath);
        return hostsFile.Contains(Header);
    }

    private static void CreateHostsIfNotExists()
    {
        if (!File.Exists(HostsPath))
        {
            Log.Information("Hosts file not found at {HostsPath}. Creating a new one with default content.", HostsPath);
            File.WriteAllText(HostsPath, DefaultHostsFile);
        }
    }

    // Default content for the hosts file, based on the standard Windows hosts file.
    // Source: https://support.microsoft.com/en-us/topic/how-to-reset-the-hosts-file-back-to-the-default-c2a43f9d-e176-c6f3-e4ef-3500277a6dae
    private const string DefaultHostsFile = @"# Copyright (c) 1993-2009 Microsoft Corp.
#
# This is a sample HOSTS file used by Microsoft TCP/IP for Windows.
#
# This file contains the mappings of IP addresses to host names. Each
# entry should be kept on an individual line. The IP address should
# be placed in the first column followed by the corresponding host name.
# The IP address and the host name should be separated by at least one
# space.
#
# Additionally, comments (such as these) may be inserted on individual
# lines or following the machine name denoted by a '#' symbol.
#
# For example:
#
#      102.54.94.97     rhino.acme.com          # source server
#       38.25.63.10     x.acme.com              # x client host
# localhost name resolution is handled within DNS itself.
#    127.0.0.1       localhost
#    ::1             localhost
";
}