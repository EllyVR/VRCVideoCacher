using System.Runtime.Versioning;
using Serilog;
using ShellLink;
using ShellLink.Structures;

namespace VRCVideoCacher.Utils;

public class AutoStartShortcut
{
    private static readonly ILogger Log = Program.Logger.ForContext<AutoStartShortcut>();
    private static readonly byte[] ShortcutSignatureBytes = { 0x4C, 0x00, 0x00, 0x00 }; // signature for ShellLinkHeader
    private const string ShortcutName = "VRCVideoCacher";
#if STEAMRELEASE
    private const string ShortcutExtension = ".url";
    private const string SteamGameUrl = "steam://rungameid/4296960";
#else
    private const string ShortcutExtension = ".lnk";
#endif
    [SupportedOSPlatform("windows")]
    public static void TryUpdateShortcutPath()
    {
        RemoveLegacyShortcut(true);

        var shortcut = GetOurShortcut();
        if (shortcut == null)
            return;

#if STEAMRELEASE
        var currentContent = File.ReadAllText(shortcut);
        var expectedContent = $"[{{000214A0-0000-0000-C000-000000000046}}]\r\n[InternetShortcut]\r\nURL={SteamGameUrl}\r\n";
        if (currentContent == expectedContent)
            return;

        Log.Information("Updating VRCX autostart shortcut URL...");
        File.WriteAllText(shortcut, expectedContent);
#else
        var info = Shortcut.ReadFromFile(shortcut);
        if (info.LinkTargetIDList.Path == Environment.ProcessPath &&
            info.StringData.WorkingDir == Path.GetDirectoryName(Environment.ProcessPath))
            return;

        Log.Information("Updating VRCX autostart shortcut path...");
        info.LinkTargetIDList.Path = Environment.ProcessPath;
        info.StringData.WorkingDir = Path.GetDirectoryName(Environment.ProcessPath);
        info.WriteToFile(shortcut);
#endif
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

        RemoveLegacyShortcut(false);

        Log.Information("Adding VRCVideoCacher to VRCX autostart...");
        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX", "startup");
        var shortcutPath = Path.Join(path, $"{ShortcutName}{ShortcutExtension}");
        if (!Directory.Exists(path))
        {
            Log.Information("VRCX isn't installed");
            return;
        }

#if STEAMRELEASE
        var content = $"[{{000214A0-0000-0000-C000-000000000046}}]\r\n[InternetShortcut]\r\nURL={SteamGameUrl}\r\n";
        File.WriteAllText(shortcutPath, content);
#else
        var shortcut = new Shortcut
        {
            LinkTargetIDList = new LinkTargetIDList
            {
                Path = Environment.ProcessPath
            },
            StringData = new StringData
            {
                WorkingDir = Path.GetDirectoryName(Environment.ProcessPath)
            }
        };
        shortcut.WriteToFile(shortcutPath);
#endif
    }

    [SupportedOSPlatform("windows")]
    private static void RemoveLegacyShortcut(bool createIfAnyFound)
    {
        var shortcutPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX", "startup");
        if (!Directory.Exists(shortcutPath))
            return;

        var shortcuts = FindShortcutFiles(shortcutPath);

#if STEAMRELEASE
        var legacyExtension = ".lnk";
#else
        var legacyExtension = ".url";
#endif
        bool foundLegacy = false;
        foreach (var shortCut in shortcuts)
        {
            if (shortCut.Contains(ShortcutName) && shortCut.EndsWith(legacyExtension, StringComparison.OrdinalIgnoreCase))
            {
                foundLegacy = true;
                Log.Information("Removing alternate shortcut {ShortCut}", shortCut);
                File.Delete(shortCut);
            }
        }

        if (createIfAnyFound && foundLegacy)
        {
            CreateShortcut();
        }
    }

    private static string? GetOurShortcut()
    {
        var shortcutPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX", "startup");
        if (!Directory.Exists(shortcutPath))
            return null;

        var shortcuts = FindShortcutFiles(shortcutPath);
        foreach (var shortCut in shortcuts)
        {
            if (shortCut.Contains(ShortcutName) && shortCut.EndsWith(ShortcutExtension, StringComparison.OrdinalIgnoreCase))
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
            if (file.Extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
                ret.Add(file.FullName);
            else if (IsShortcutFile(file.FullName))
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