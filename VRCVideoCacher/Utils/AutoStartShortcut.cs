using System.Runtime.Versioning;
using Serilog;
using ShellLink;
using ShellLink.Structures;

namespace VRCVideoCacher.Utils;

public class AutoStartShortcut
{
    private static readonly ILogger Log = Program.Logger.ForContext<AutoStartShortcut>();
    private const string ShortcutName = "VRCVideoCacher";
    private static readonly byte[] ShortcutSignatureBytes = { 0x4C, 0x00, 0x00, 0x00 }; // signature for ShellLinkHeader
    private static readonly byte[] UrlShortcutHeader = "[{000214A0-0000-0000-C000-000000000046}]"u8.ToArray(); // .url file header
    private const string SteamUrlShortcut = """
                                            [{000214A0-0000-0000-C000-000000000046}]
                                            Prop3=19,0
                                            [InternetShortcut]
                                            IDList=
                                            IconIndex=0
                                            URL=steam://rungameid/4296960
                                            IconFile=C:\Program Files (x86)\Steam\steam\games\bace110d022b726f540557fddd1000aeccd9b9a9.ico
                                            """;


    [SupportedOSPlatform("windows")]
    public static void TryUpdateShortcutPath()
    {
        var (shortcut, steamShortcut) = GetOurShortcut();
        if (shortcut == null && steamShortcut == null)
            return;

#if STEAMRELEASE
        if (shortcut != null)
        {
            Log.Information("Updating VRCX autostart shortcut path with Steam URL...");
            File.Delete(shortcut);
            File.WriteAllText(shortcut.Replace(".lnk", ".url"), SteamUrlShortcut);
        }
        return;
#endif

        if (steamShortcut != null)
        {
            Log.Information("Updating VRCX autostart shortcut path with local URL...");
            File.Delete(steamShortcut);
            CreateShortcut();
            return;
        }

        var info = Shortcut.ReadFromFile(shortcut);
        if (info.LinkTargetIDList.Path == Environment.ProcessPath &&
            info.StringData.WorkingDir == Path.GetDirectoryName(Environment.ProcessPath))
            return;

        Log.Information("Updating VRCX autostart shortcut path...");
        info.LinkTargetIDList.Path = Environment.ProcessPath;
        info.StringData.WorkingDir = Path.GetDirectoryName(Environment.ProcessPath);
        info.WriteToFile(shortcut);
    }

    private static bool StartupEnabled()
    {
        var (shortcut, steamShortcut) = GetOurShortcut();
        if (string.IsNullOrEmpty(shortcut) && string.IsNullOrEmpty(steamShortcut))
            return false;

        return true;
    }

    [SupportedOSPlatform("windows")]
    public static void CreateShortcut()
    {
        if (StartupEnabled())
            return;

        Log.Information("Adding VRCVideoCacher to VRCX autostart...");
        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX", "startup");
        var shortcutPath = Path.Join(path, $"{ShortcutName}.lnk");
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
                WorkingDir = Path.GetDirectoryName(Environment.ProcessPath)
            }
        };
        shortcut.WriteToFile(shortcutPath);
    }

    private static Tuple<string?, string?> GetOurShortcut()
    {
        var shortcutPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX", "startup");
        if (!Directory.Exists(shortcutPath))
            return new Tuple<string?, string?>(null, null);

        var (shortcutFiles, urlShortcuts) = FindShortcutFiles(shortcutPath);
        foreach (var shortCut in shortcutFiles)
        {
            if (shortCut.Contains(ShortcutName))
                return new Tuple<string?, string?>(shortCut, null);
        }
        foreach (var urlShortcut in urlShortcuts)
        {
            try
            {
                const string ourUrl = "URL=steam://rungameid/4296960";
                var lines = File.ReadAllLines(urlShortcut);
                var urlLine = lines.FirstOrDefault(l => l == ourUrl);
                if (urlLine == null)
                    continue;
                        
                return new Tuple<string?, string?>(null, urlShortcut);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error reading shortcut file: {0}", urlShortcut);
            }
        }

        return new Tuple<string?, string?>(null, null);
    }

    private static Tuple<List<string>, List<string>> FindShortcutFiles(string folderPath)
    {
        var directoryInfo = new DirectoryInfo(folderPath);
        var files = directoryInfo.GetFiles();
        var shortcuts = new List<string>();
        var urlShortcuts = new List<string>();

        foreach (var file in files)
        {
            if (IsShortcutFile(file.FullName))
            {
                shortcuts.Add(file.FullName);
                continue;
            }
            if (IsUrlShortcutFile(file.FullName))
            {
                urlShortcuts.Add(file.FullName);
            }
        }

        return new Tuple<List<string>, List<string>>(shortcuts, urlShortcuts);
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

    private static bool IsUrlShortcutFile(string filePath)
    {
        var headerBytes = new byte[UrlShortcutHeader.Length];
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fileStream.Length < headerBytes.Length)
            return false;
        fileStream.ReadExactly(headerBytes, 0, headerBytes.Length);

        return headerBytes.SequenceEqual(UrlShortcutHeader);
    }
}