namespace VRCVideoCacher.Services;

public class ThumbnailManager
{
    public static readonly string CacheDir = Path.Join(Program.DataPath, "MetadataCache");
    public static readonly string ThumbnailCacheDir = Path.Join(CacheDir, "thumbnails");

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } },
        Timeout = TimeSpan.FromSeconds(10)
    };

    public ThumbnailManager()
    {
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(ThumbnailCacheDir);
    }

    public static string GetThumbnailPath(string videoId)
    {
        return Path.Join(ThumbnailCacheDir, $"{videoId}.jpg");
    }

    public static string? GetThumbnail(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        var localPath = GetThumbnailPath(videoId);
        return File.Exists(localPath) ? localPath : null;
    }

    public static async Task<string?> TrySaveThumbnail(string videoId, string url)
    {
        try
        {
            var thumbnailPath = GetThumbnailPath(videoId);
            if (File.Exists(thumbnailPath))
                return null;

            var data = await HttpClient.GetStreamAsync(url);
            await using var fileStream = new FileStream(thumbnailPath, FileMode.Create, FileAccess.Write);
            await data.CopyToAsync(fileStream);
            return thumbnailPath;
        }
        catch
        {
            // Silently fail - thumbnail is not critical
            return null;
        }
    }
}
