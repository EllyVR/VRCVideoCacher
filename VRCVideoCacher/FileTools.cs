using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Serilog;
using ValveKeyValue;

namespace VRCVideoCacher;

public class FileTools
{
    private static readonly ILogger Log = Program.Logger.ForContext<FileTools>();
    private static readonly string? YtdlPathVrc;
    private static readonly string? BackupPathVrc;
    private static readonly string? YtdlPathReso;
    private static readonly string? BackupPathReso;
    private static readonly ImmutableList<string> SteamPaths = [".var/app/com.valvesoftware.Steam", ".steam/steam", ".steam/debian-installation", ".local/share/Steam"];
    private const string ResoniteAppId = "2519830";
    private const string VrcAppId = "438100";

    static FileTools()
    {
        string? resoPath;
        if (!string.IsNullOrEmpty(ConfigManager.Config.ResonitePath))
        {
            resoPath = ConfigManager.Config.ResonitePath;
        }
        else
        {
            resoPath = GetAppLibraryPath(ResoniteAppId)?
                .Select(path => Path.Join(path, "steamapps", "common", "Resonite"))?
                .Where(Path.Exists)?
                .First();
        }
        if (!string.IsNullOrEmpty(resoPath))
        {
            YtdlPathReso = Path.Join(resoPath, "RuntimeData", OperatingSystem.IsLinux() ? "yt-dlp_linux" : "yt-dlp.exe");
            BackupPathReso = $"{YtdlPathReso}.bkp";
        }

        string localLowPath;
        if (OperatingSystem.IsWindows())
        {
            localLowPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low";
        }
        else if (OperatingSystem.IsLinux())
        {
            var compatPath = GetCompatPath(VrcAppId) ?? throw new Exception("Unable to find VRChat compat data");
            localLowPath = Path.Join(compatPath, "pfx/drive_c/users/steamuser/AppData/LocalLow");
        }
        else
        {
            throw new NotImplementedException("Unknown platform");
        }
        var vrcPath = Path.Join(localLowPath, "VRChat", "VRChat", "Tools", "yt-dlp.exe");
        if (!File.Exists(vrcPath))
        {
            Log.Warning("YT-DLP not found at expected VRChat path: {Path}", vrcPath);
        }
        else
        {
            YtdlPathVrc = vrcPath;
            BackupPathVrc = $"{vrcPath}.bkp";
        }
    }

    private static List<string>? GetAppLibraryPath(string appid)
    {
        string vdfPath;
        if (OperatingSystem.IsWindows())
        {
            string? steamInstallPath = (string?)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", "");
            if (string.IsNullOrEmpty(steamInstallPath))
            {
                Log.Error("GetAppLibraryPath: Unable to find Steam installation directory");
                return null;
            }
            vdfPath = Path.Join(steamInstallPath, "steamapps", "libraryfolders.vdf");
        }
        else if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var vdfPaths = SteamPaths
                .Select(path => Path.Join(home, path, "steamapps", "libraryfolders.vdf"))
                .Where(Path.Exists);
            if (vdfPaths.Count() == 0)
            {
                Log.Error("GetAppLibraryPath: Couldn't find libraryfolders.vdf!");
                return null;
            }

            vdfPath = vdfPaths.First();
        }
        else
        {
            Log.Error("GetAppLibraryPath: Unsupported operating system {OperatingSystem}", Environment.OSVersion.Platform);
            return null;
        }

        Log.Debug("GetAppLibraryPath: Using VDF path {VdfPath}", vdfPath);

        List<string> libraryPaths = [];
        try
        {
            var stream = File.OpenRead(vdfPath);
            KVObject data = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
            foreach (var folder in data)
            {
                var apps = (IEnumerable<KVObject>)folder["apps"];
                if (apps.Any(app => app.Name == appid))
                    libraryPaths.Add(folder["path"].ToString(CultureInfo.InvariantCulture));
            }
        }
        catch (Exception e)
        {
            Log.Error("GetAppLibraryPath: Exception while reading libraryfolders.vdf: {Error}", e.Message);
            return null;
        }

        libraryPaths = [.. libraryPaths.Where(Path.Exists)];

        if (libraryPaths.Count == 0)
        {
            Log.Error("Failed to find library path for Steam app {AppId}.", appid);
            return null;
        }
        return libraryPaths;
    }

    [SupportedOSPlatform("linux")]
    private static string? GetCompatPath(string appid)
    {
        var libraryPaths = GetAppLibraryPath(appid);
        if (libraryPaths == null) return null;

        var paths = libraryPaths
            .Select(path => Path.Join(path, "steamapps", "compatdata", appid))
            .Where(Path.Exists)
            .ToImmutableList();
        return paths.Count > 0 ? paths.First() : null;
    }

    public static string? LocateFile(string filename)
    {
        var systemPath = Environment.GetEnvironmentVariable("PATH");
        if (systemPath == null) return null;

        var systemPaths = systemPath.Split(Path.PathSeparator);

        var paths = systemPaths
            .Select(path => Path.Join(path, filename))
            .Where(Path.Exists)
            .ToImmutableList();
        return paths.Count > 0 ? paths.First() : null;
    }

    public static void MarkFileExecutable(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            mode |= UnixFileMode.UserExecute;
            File.SetUnixFileMode(path, mode);
        }
    }

    public static void BackupAllYtdl()
    {
        if (ConfigManager.Config.PatchVrChat)
        {
            Log.Information("Patching VRChat yt-dlp");
            if (!BackupAndReplaceYtdl(YtdlPathVrc, BackupPathVrc, false))
                Log.Error("Can't find VRC data, it may not be installed. {Path}", YtdlPathVrc);
        }
        if (ConfigManager.Config.PatchResonite)
        {
            Log.Information("Patching Resonite yt-dlp");
            if (!BackupAndReplaceYtdl(YtdlPathReso, BackupPathReso, OperatingSystem.IsLinux()))
                Log.Warning("Can't find Resonite data, it may not be installed. {Path}", YtdlPathVrc);
        }
    }

    public static void RestoreAllYtdl()
    {
        RestoreYtdl(YtdlPathVrc, BackupPathVrc);
        RestoreYtdl(YtdlPathReso, BackupPathReso);
    }

    private static bool BackupAndReplaceYtdl(string? ytdlPath, string? backupPath, bool useLinuxStub)
    {
        if (string.IsNullOrEmpty(ytdlPath) ||
            string.IsNullOrEmpty(backupPath) ||
            !Directory.Exists(Path.GetDirectoryName(ytdlPath)))
        {
            return false;
        }
        if (File.Exists(ytdlPath))
        {
            var hash = Program.ComputeBinaryContentHash(File.ReadAllBytes(ytdlPath));
            if (hash == Program.GetYtdlpHash(useLinuxStub))
            {
                Log.Information("YT-DLP is already patched.");
                return true;
            }
            if (File.Exists(backupPath))
            {
                File.SetAttributes(backupPath, FileAttributes.Normal);
                File.Delete(backupPath);
            }
            File.Move(ytdlPath, backupPath);
            Log.Information("Backed up YT-DLP.");
        }
        using var stream = Program.GetYtDlpStub(useLinuxStub);
        using var fileStream = File.Create(ytdlPath);
        stream.CopyTo(fileStream);
        fileStream.Close();
        var attr = File.GetAttributes(ytdlPath);
        attr |= FileAttributes.ReadOnly;
        File.SetAttributes(ytdlPath, attr);
        MarkFileExecutable(ytdlPath);
        Log.Information("Patched YT-DLP.");
        return true;
    }

    private static void RestoreYtdl(string? ytdlPath, string? backupPath)
    {
        if (string.IsNullOrEmpty(ytdlPath) ||
            string.IsNullOrEmpty(backupPath) ||
            !File.Exists(backupPath))
            return;

        Log.Information("Restoring yt-dlp...");
        if (File.Exists(ytdlPath))
        {
            File.SetAttributes(ytdlPath, FileAttributes.Normal);
            File.Delete(ytdlPath);
        }
        File.Move(backupPath, ytdlPath);
        var attr = File.GetAttributes(ytdlPath);
        attr &= ~FileAttributes.ReadOnly;
        File.SetAttributes(ytdlPath, attr);
        Log.Information("Restored YT-DLP.");
    }
}
