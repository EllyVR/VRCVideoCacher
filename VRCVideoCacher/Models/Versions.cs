using Newtonsoft.Json;

namespace VRCVideoCacher.Models;

public static class Versions
{
    private static readonly string VersionPath = Path.Combine(Program.DataPath, "version.json");
    public static VersionJson CurrentVersion = new();
    
    static Versions()
    {
        var oldVersionFile = Path.Combine(Program.DataPath, "yt-dlp.version.txt");
        if (File.Exists(oldVersionFile))
        {
            CurrentVersion = new VersionJson
            {
                ytdlp = File.ReadAllText(oldVersionFile).Trim(),
                ffmpeg = string.Empty,
                deno = string.Empty
            };
            File.Delete(oldVersionFile);
            Save();
            return;
        }

        VersionJson? versions = null;
        if (File.Exists(VersionPath))
            versions = JsonConvert.DeserializeObject<VersionJson>(File.ReadAllText(VersionPath));

        if (versions != null)
        {
            CurrentVersion = versions;
            return;
        }
        Save();
    }
    
    public static void Save()
    {
        File.WriteAllText(VersionPath, JsonConvert.SerializeObject(CurrentVersion, Formatting.Indented));
    }
}

public class VersionJson
{
    public string ytdlp { get; set; } = string.Empty;
    public string ffmpeg { get; set; } = string.Empty;
    public string deno { get; set; } = string.Empty;
}