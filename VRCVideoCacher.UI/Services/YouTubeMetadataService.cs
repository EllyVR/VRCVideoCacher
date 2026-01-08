using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRCVideoCacher;

namespace VRCVideoCacher.UI.Services;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class MetadataJsonContext : JsonSerializerContext
{
}

public static class YouTubeMetadataService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly ConcurrentDictionary<string, string> TitleCache = new();
    private static readonly string CacheDir = Path.Combine(VRCVideoCacher.Program.DataPath, "MetadataCache");
    private static readonly string TitleCacheFile = Path.Combine(CacheDir, "titles.json");
    private static readonly string ThumbnailCacheDir = Path.Combine(CacheDir, "thumbnails");
    private static bool _cacheLoaded;

    static YouTubeMetadataService()
    {
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(ThumbnailCacheDir);
    }

    private static void LoadCacheFromDisk()
    {
        if (_cacheLoaded) return;
        _cacheLoaded = true;

        try
        {
            if (File.Exists(TitleCacheFile))
            {
                var json = File.ReadAllText(TitleCacheFile);
                var cached = JsonSerializer.Deserialize(json, MetadataJsonContext.Default.DictionaryStringString);
                if (cached != null)
                {
                    foreach (var kvp in cached)
                    {
                        TitleCache.TryAdd(kvp.Key, kvp.Value);
                    }
                }
            }
        }
        catch
        {
            // Ignore cache load errors
        }
    }

    private static void SaveCacheToDisk()
    {
        try
        {
            var dict = TitleCache.ToDictionary(k => k.Key, v => v.Value);
            var json = JsonSerializer.Serialize(dict, MetadataJsonContext.Default.DictionaryStringString);
            File.WriteAllText(TitleCacheFile, json);
        }
        catch
        {
            // Ignore cache save errors
        }
    }

    public static async Task<string?> GetVideoTitleAsync(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        LoadCacheFromDisk();

        // Check cache first
        if (TitleCache.TryGetValue(videoId, out var cachedTitle))
            return cachedTitle;

        try
        {
            var url = $"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={videoId}&format=json";
            var response = await HttpClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("title", out var titleElement))
            {
                var title = titleElement.GetString();
                if (!string.IsNullOrEmpty(title))
                {
                    TitleCache[videoId] = title;
                    SaveCacheToDisk();
                    return title;
                }
            }
        }
        catch
        {
            // Silently fail - we'll just show the video ID
        }

        return null;
    }

    public static string GetThumbnailPath(string videoId)
    {
        return Path.Combine(ThumbnailCacheDir, $"{videoId}.jpg");
    }

    public static async Task<string?> GetCachedThumbnailAsync(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        var localPath = GetThumbnailPath(videoId);

        // Return cached thumbnail if exists
        if (File.Exists(localPath))
            return localPath;

        // Download and cache thumbnail
        try
        {
            var url = $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg";
            var imageBytes = await HttpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(localPath, imageBytes);
            return localPath;
        }
        catch
        {
            // Fall back to remote URL
            return null;
        }
    }
}
