using Newtonsoft.Json;
using Serilog;

namespace VRCVideoCacher.Models;

public class Versions
{
    private static readonly ILogger Log = Program.Logger.ForContext<Versions>();
    private static readonly string VersionPath = Path.Join(Program.DataPath, "version.json");
    public static readonly VersionJson CurrentVersion = new();

    static Versions()
    {
        if (File.Exists(VersionPath))
        {
            try
            {
                CurrentVersion = JsonConvert.DeserializeObject<VersionJson>(File.ReadAllText(VersionPath)) ??
                                 new VersionJson();
                return;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse version file, it may be corrupted. Recreating...");
            }
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