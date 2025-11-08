using System.Collections.Immutable;
using System.Globalization;
using Serilog;
using ValveKeyValue;

namespace VRCVideoCacher;

public class FileTools
{
    private static readonly ILogger Log = Program.Logger.ForContext<FileTools>();
    private static readonly string YtdlPathVRC;
    private static readonly string BackupPathVRC;
    private static readonly string YtdlPathReso;
    private static readonly string BackupPathReso;
    private static readonly ImmutableList<string> SteamPaths = [".var/app/com.valvesoftware.Steam", ".steam/steam", ".local/share/Steam"];

    static FileTools()
    {
        


        YtdlPathReso = $"{GetResonitePath()}\\steamapps\\common\\Resonite\\RuntimeData\\yt-dlp.exe";
        BackupPathReso = $"{GetResonitePath()}\\steamapps\\common\\Resonite\\RuntimeData\\yt-dlp.exe.bkp";

        string localLowPath;
        if (OperatingSystem.IsWindows())
        { 
            localLowPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low";
        }
        else if (OperatingSystem.IsLinux())
        { 
            var compatPath = GetCompatPath("438100") ?? throw new Exception("Unable to find VRChat compat data"); 
            localLowPath = Path.Join(compatPath, "pfx/drive_c/users/steamuser/AppData/LocalLow");
        }
        else
        { 
            throw new NotImplementedException("Unknown platform");
        }
        YtdlPathVRC = Path.Join(localLowPath, "VRChat/VRChat/Tools/yt-dlp.exe");
        BackupPathVRC = Path.Join(localLowPath, "VRChat/VRChat/Tools/yt-dlp.exe.bkp");

        
    }

    private static string? GetResonitePath()
    {
        string appid = "2519830";
        if (OperatingSystem.IsWindows())
        {
            var libfolders = "C:\\Program Files (x86)\\Steam\\steamapps\\libraryfolders.vdf";
            var stream = File.OpenRead(libfolders);
            KVObject data = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
            List<string> libraryPaths = [];
            foreach (var folder in data)
            {
                var apps = (IEnumerable<KVObject>)folder["apps"];
                if (apps.Any(app => app.Name == appid))
                {
                    return folder["path"].ToString(CultureInfo.InvariantCulture);
                }
            }

        }

        return null;
    }
    // Linux only
    private static string? GetCompatPath(string appid)
    {
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("GetCompatPath is only supported on Linux");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var steamPaths = SteamPaths.Select(path => Path.Join(home, path))
            .Where(Path.Exists);
        var steam = steamPaths.First();
        if (!Path.Exists(steam))
        {
            Log.Error("Steam folder doesn't exist!");
            return null;
        }

        Log.Debug("Using steam path: {Steam}", steam);
        var libraryfolders = Path.Join(steam, "steamapps/libraryfolders.vdf");
        var stream = File.OpenRead(libraryfolders);

        KVObject data = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);

        List<string> libraryPaths = [];
        foreach (var folder in data)
        {
            // var label = folder["label"]?.ToString(CultureInfo.InvariantCulture);
            // var name = string.IsNullOrEmpty(label) ? folder.Name : label;
            // See https://github.com/ValveResourceFormat/ValveKeyValue/issues/30#issuecomment-1581924891
            var apps = (IEnumerable<KVObject>)folder["apps"];
            if (apps.Any(app => app.Name == appid))
                libraryPaths.Add(folder["path"].ToString(CultureInfo.InvariantCulture));
        }

        var paths = libraryPaths
            .Select(path => Path.Join(path, $"steamapps/compatdata/{appid}"))
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
            .Select(path => Path.Combine(path, filename))
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

    public static void BackupAndReplaceVRC() => BackupAndReplaceYtdl(YtdlPathVRC, YtdlPathReso);
    
    public static void BackupAndReplaceReso() => BackupAndReplaceYtdl(BackupPathReso, BackupPathReso);
    
    public static void RestoreVRC() => Restore(YtdlPathVRC, BackupPathVRC);
    
    public static void RestoreReso() => Restore(BackupPathReso, BackupPathReso);
    
    private static void BackupAndReplaceYtdl(string YtdlPath, string BackupPath)
    {
        if (!Directory.Exists(ConfigManager.UtilsPath))
        {
            Log.Error("YT-DLP directory does not exist, VRChat may not be installed.");
            return;
        }
        if (File.Exists(YtdlPath))
        {
            var hash = Program.ComputeBinaryContentHash(File.ReadAllBytes(YtdlPath));
            if (hash == Program.YtdlpHash)
            {
                Log.Information("YT-DLP is already patched.");
                return;
            }
            if (File.Exists(BackupPath))
            {
                File.SetAttributes(BackupPath, FileAttributes.Normal);
                File.Delete(BackupPath);
            }
            File.Move(YtdlPath, BackupPath);
            Log.Information("Backed up YT-DLP.");
        }
        using var stream = Program.GetYtDlpStub();
        using var fileStream = File.Create(YtdlPath);
        stream.CopyTo(fileStream);
        fileStream.Close();
        var attr = File.GetAttributes(YtdlPath);
        attr |= FileAttributes.ReadOnly;
        File.SetAttributes(YtdlPath, attr);
        Log.Information("Patched YT-DLP.");
    }

    private static void Restore(string YtdlPath, string BackupPath)
    {
        Log.Information("Restoring yt-dlp...");
        if (!File.Exists(BackupPath))
            return;
        
        if (File.Exists(YtdlPath))
        {
            File.SetAttributes(YtdlPath, FileAttributes.Normal);
            File.Delete(YtdlPath);
        }
        File.Move(BackupPath, YtdlPath);
        var attr = File.GetAttributes(YtdlPath);
        attr &= ~FileAttributes.ReadOnly;
        File.SetAttributes(YtdlPath, attr);
        Log.Information("Restored YT-DLP.");
    }
}