using System.Text;

namespace VRCVideoCacher.Elevator;

public class HostsManager
{
    private static readonly string Header = @$"# ----- BEGIN VRCVIDEOCACHER ----- ";
    private static readonly string Footer = @$"# ----- END VRCVIDEOCACHER ----- ";
    private static readonly string HostsPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.System)}/drivers/etc/hosts";

    public static void Add()
    {
        try
        {
            string HostFile = File.ReadAllText(HostsPath);
            if (HostFile.Contains(Header))
            {
                return;
            }


            File.AppendAllText(HostsPath,
                $"{Environment.NewLine}{Header}{Environment.NewLine}127.0.0.1 localhost.youtube.com{Environment.NewLine}{Footer}{Environment.NewLine}");
            
            
        }
        catch (Exception e)
        {
            
        }
    }

    public static void Remove()
    {
        try
        {
            string hostsFile = File.ReadAllText(HostsPath);
            if (!hostsFile.Contains(Header))
            {
                return;
            }

            int headerStart = hostsFile.IndexOf(Header, StringComparison.Ordinal) - Environment.NewLine.Length;
            int headerEnd = hostsFile.IndexOf(Footer, StringComparison.Ordinal) +
                            Footer.Length;
            string newHostsFile = hostsFile.Remove(headerStart, headerEnd - headerStart);
            File.WriteAllText(HostsPath, newHostsFile);
        }
        catch (Exception e)
        {
            
        }
        
    }
}