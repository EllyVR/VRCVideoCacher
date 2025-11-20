using System.Runtime.Versioning;
using Serilog;
using ShellLink;
using ShellLink.Structures;

namespace VRCVideoCacher;

public class AutoStartShortcut
{
    private static readonly ILogger Log = Program.Logger.ForContext<AutoStartShortcut>();
    private static readonly byte[] ShortcutSignatureBytes = { 0x4C, 0x00, 0x00, 0x00 }; // signature for ShellLinkHeader
    private const string ShortcutName = "VRCVideoCacher";
    
    [SupportedOSPlatform("windows")]
    public static void TryUpdateShortcutPath()
    {
        var shortcut = GetOurShortcut();
        if (shortcut == null)
            return;

        var info = Shortcut.ReadFromFile(shortcut);
        if (info.LinkTargetIDList.Path == Environment.ProcessPath)
            return;
        
        Log.Information("Updating VRCX autostart shortcut path...");
        info.LinkTargetIDList.Path = Environment.ProcessPath;
        info.WriteToFile(shortcut);
    }

    private static bool StartupEnabled()
    {
        if (string.IsNullOrEmpty(GetOurShortcut()))
            return false;

        return true;
    }

    [SupportedOSPlatform("windows")]
    public static void CreateShortcut()
    {
        if (StartupEnabled())
            return;
        
        Log.Information("Adding VRCVideoCacher to VRCX autostart...");
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX", "startup");
        var shortcutPath = Path.Combine(path, $"{ShortcutName}.lnk");
        if (!Directory.Exists(path))
        {
            Log.Information("VRCX isn't installed");
            return;
        }
        
        var shortcut = new Shortcut
        {
            LinkTargetIDList = new LinkTargetIDList
            {
                Path = Environment.ProcessPath
            },
            StringData = new StringData
            {
                WorkingDir = Path.GetDirectoryName(Environment.ProcessPath),
            }
        };
        shortcut.WriteToFile(shortcutPath);
    }

    private static string? GetOurShortcut()
    {
        var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX", "startup");
        if (!Directory.Exists(shortcutPath))
            return null;
        
        var shortcuts = FindShortcutFiles(shortcutPath);
        foreach(var shortCut in shortcuts)
        {
            if (shortCut.Contains(ShortcutName))
                return shortCut;
        }

        return null;
    }
    
    private static List<string> FindShortcutFiles(string folderPath)
    {
        var directoryInfo = new DirectoryInfo(folderPath);
        var files = directoryInfo.GetFiles();
        var ret = new List<string>();

        foreach (var file in files)
        {
            if (IsShortcutFile(file.FullName))
                ret.Add(file.FullName);
        }

        return ret;
    }
    
    private static bool IsShortcutFile(string filePath)
    {
        var headerBytes = new byte[4];
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fileStream.Length >= 4)
        {
            fileStream.ReadExactly(headerBytes, 0, 4);
        }

        return headerBytes.SequenceEqual(ShortcutSignatureBytes);
    }
}