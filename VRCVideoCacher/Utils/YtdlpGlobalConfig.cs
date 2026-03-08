using Serilog;

namespace VRCVideoCacher.Utils;

public class YtdlpGlobalConfig
{
    private static readonly ILogger Log = Program.Logger.ForContext<YtdlpGlobalConfig>();

    private static readonly List<string> YtdlConfigPaths =
    [
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "yt-dlp.conf"),
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "yt-dlp", "config"),
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "yt-dlp", "config.txt"),
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "yt-dlp", "config"),
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "yt-dlp", "config.txt"),
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "yt-dlp.conf"),
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "yt-dlp.conf.txt"),
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "yt-dlp/config"),
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "yt-dlp/config.txt"),
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yt-dlp/config"),
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".yt-dlp/config.txt"),
    ];

    public static bool GlobalYtdlConfigExists()
    {
        return YtdlConfigPaths.Any(File.Exists);
    }

    public static void DeleteGlobalYtdlConfig()
    {
        foreach (var configPath in YtdlConfigPaths)
        {
            if (File.Exists(configPath))
            {
                Log.Information("Deleting global YT-DLP config: {ConfigPath}", configPath);
                File.Delete(configPath);
            }
        }
    }
}