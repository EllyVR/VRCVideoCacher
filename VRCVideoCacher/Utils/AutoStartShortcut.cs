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
    private const string SteamShortcutExtension = ".url";
    private const string SteamGameUrl = "steam://rungameid/4296960";
    private const string ExeShortcutExtension = ".lnk";
    private static bool? _doesVrcxSupportSteamShortcut = null;

    [SupportedOSPlatform("windows")]
    public static void TryUpdateShortcutPath()
    {
        RemoveLegacyShortcut(true);

        var shortcut = GetOurShortcut();
        if (shortcut == null)
            return;

        if (ShouldUseSteamShortcut())
        {
            var currentContent = File.ReadAllText(shortcut);
            var expectedContent = $"[{{000214A0-0000-0000-C000-000000000046}}]\r\n[InternetShortcut]\r\nURL={SteamGameUrl}\r\n";
            if (currentContent == expectedContent)
                return;

            Log.Information("Updating VRCX autostart shortcut URL...");
            File.WriteAllText(shortcut, expectedContent);
        }
        else
        {
            var info = Shortcut.ReadFromFile(shortcut);
            if (info.LinkTargetIDList.Path == Environment.ProcessPath &&
                info.StringData.WorkingDir == Path.GetDirectoryName(Environment.ProcessPath))
                return;

            Log.Information("Updating VRCX autostart shortcut path...");
            info.LinkTargetIDList.Path = Environment.ProcessPath;
            info.StringData.WorkingDir = Path.GetDirectoryName(Environment.ProcessPath);
            info.WriteToFile(shortcut);
        }
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
        var shortcutPath = Path.Join(path, $"{ShortcutName}{(_doesVrcxSupportSteamShortcut == true ? SteamShortcutExtension : ExeShortcutExtension)}");
        if (!Directory.Exists(path))
        {
            Log.Information("VRCX isn't installed");
            return;
        }

        if (ShouldUseSteamShortcut())
        {
            var content = $"[{{000214A0-0000-0000-C000-000000000046}}]\r\n[InternetShortcut]\r\nURL={SteamGameUrl}\r\n";
            File.WriteAllText(shortcutPath, content);
        }
        else
        {
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
    }

    [SupportedOSPlatform("windows")]
    private static void RemoveLegacyShortcut(bool createIfAnyFound)
    {
        var shortcutPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX", "startup");
        if (!Directory.Exists(shortcutPath))
            return;

        var shortcuts = FindShortcutFiles(shortcutPath);

        string legacyExtension = ShouldUseSteamShortcut() ? ExeShortcutExtension : SteamShortcutExtension;
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
            if (shortCut.Contains(ShortcutName) && shortCut.EndsWith(_doesVrcxSupportSteamShortcut == true ? SteamShortcutExtension : ExeShortcutExtension, StringComparison.OrdinalIgnoreCase))
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

    [SupportedOSPlatform("windows")]
    private static bool ShouldUseSteamShortcut()
    {
        if(!_doesVrcxSupportSteamShortcut.HasValue)
        {
            if (TryGetVrcxVersion(out var version))
            {
                Log.Information("Detected VRCX version: {Version}", version);
                if (TryParseVrcxVersion(version, out var year, out var month, out var day))
                {
                    // Only don't use the steam shortcut if we know for certain that the VRCX version is older than 2026.3.14, which is when the url method was completed.
                    // If we can't parse the version, or if it's newer than that, we'll just use the new method and assume it will work.
                    if (year <= 2026 && month <= 3 && day < 14)
                    {
                        _doesVrcxSupportSteamShortcut = false;
                    }
                }
            }

            if (!_doesVrcxSupportSteamShortcut.HasValue)
            {
                _doesVrcxSupportSteamShortcut = true;
            }
        }

        return _doesVrcxSupportSteamShortcut.Value;

    }

    private static bool TryParseVrcxVersion(string? version, out int year, out int month, out int day)
    {
        year = 0;
        month = 0;
        day = 0;

        if (string.IsNullOrWhiteSpace(version))
            return false;

        try
        {
            if (version.Contains("T"))
            {
                var dateEnd = version.IndexOf('T');
                if (dateEnd > 0)
                {
                    var datePart = version.Substring(0, dateEnd);
                    var parts = datePart.Split('-');
                    if (parts.Length == 3 &&
                        int.TryParse(parts[0], out year) &&
                        int.TryParse(parts[1], out month) &&
                        int.TryParse(parts[2], out day))
                    {
                        return true;
                    }
                }
            }
            else if (version.Contains('.'))
            {
                var parts = version.Split('.');
                if (parts.Length >= 3 &&
                    int.TryParse(parts[0], out year) &&
                    int.TryParse(parts[1], out month) &&
                    int.TryParse(parts[2], out day))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error parsing VRCX version: {Version}", version);
        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryGetVrcxVersion(out string? version)
    {
        version = null;

        try
        {
            // Check Windows registry for installed applications
            string[] registryPaths = new[]
            {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };

            foreach (var regPath in registryPaths)
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath))
                {
                    if (key != null)
                    {
                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            using (var subKey = key.OpenSubKey(subKeyName))
                            {
                                var displayName = subKey?.GetValue("DisplayName") as string;
                                if (subKey != null && displayName != null && displayName.Contains("VRCX", StringComparison.OrdinalIgnoreCase))
                                {
                                    var installLocation = subKey.GetValue("InstallLocation") as string;
                                    if (!string.IsNullOrWhiteSpace(installLocation))
                                    {
                                        if (TryGetVrcxVersionFromFile(installLocation, out version))
                                        {
                                            return true;
                                        }
                                    }
                                    else
                                    {
                                        // Try DisplayIcon as fallback
                                        var displayIcon = subKey.GetValue("DisplayIcon") as string;
                                        displayIcon = displayIcon?.Trim('"');
                                        if (!string.IsNullOrWhiteSpace(displayIcon) && displayIcon.EndsWith("VRCX.ico", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (TryGetVrcxVersionFromFile(Path.GetDirectoryName(displayIcon), out version))
                                            {
                                                return true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error searching registry for VRCX");
        }

        System.Diagnostics.Process[]? processes = null;
        try
        {
            processes = System.Diagnostics.Process.GetProcessesByName("VRCX");

            if (processes != null)
            {
                foreach (var proc in processes)
                {
                    try
                    {
                        var vrcxPath = proc?.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(vrcxPath))
                        {
                            if (TryGetVrcxVersionFromFile(Path.GetDirectoryName(vrcxPath), out version))
                            {
                                return true;
                            }
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error accessing process module for VRCX");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error searching for VRCX processes");
        }
        finally
        {
            if (processes != null)
            {
                foreach (var proc in processes)
                {
                    proc.Dispose();
                }
            }
        }

        return false;
    }

    private static bool TryGetVrcxVersionFromFile(string? directory, out string? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        var filePath = Path.Join(directory, "Version");
        try
        {
            if (File.Exists(filePath))
            {
                var versionText = File.ReadAllText(filePath).Trim();
                if(!string.IsNullOrWhiteSpace(versionText))
                {
                    version = versionText;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error getting VRCX version from file: {FilePath}", filePath);
        }
        return false;
    }
}