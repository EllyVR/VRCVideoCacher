using System.Collections.Concurrent;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher;

public class CacheManager
{
    private static readonly ILogger Log = Program.Logger.ForContext<CacheManager>();
    private static readonly ConcurrentDictionary<string, VideoCache> CachedAssets = new();

    static CacheManager()
    {
        BuildCache();
    }
    
    public static void Init()
    {
        TryFlushCache();
    }
    
    private static void BuildCache()
    {
        CachedAssets.Clear();
        var files = Directory.GetFiles(ConfigManager.Config.CachedAssetPath);
        foreach (var path in files)
        {
            var file = Path.GetFileName(path);
            AddToCache(file);
        }
    }
    
    private static void TryFlushCache()
    {
        if (ConfigManager.Config.CacheMaxSizeInGb <= 0f)
            return;
        
        var maxCacheSize = (long)(ConfigManager.Config.CacheMaxSizeInGb * 1024f * 1024f * 1024f);
        var cacheSize = GetCacheSize();
        if (cacheSize < maxCacheSize)
            return;

        var oldestFiles = CachedAssets.OrderBy(x => x.Value.LastModified).ToList();
        while (cacheSize >= maxCacheSize && oldestFiles.Count > 0)
        {
            var oldestFile = oldestFiles.First();
            var filePath = Path.Combine(ConfigManager.Config.CachedAssetPath, oldestFile.Value.FileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                cacheSize -= oldestFile.Value.Size;
            }
            CachedAssets.TryRemove(oldestFile.Key, out _);
            oldestFiles.RemoveAt(0);
        }
    }

    public static void AddToCache(string fileName)
    {
        var filePath = Path.Combine(ConfigManager.Config.CachedAssetPath, fileName);
        if (!File.Exists(filePath))
            return;
        
        var fileInfo = new FileInfo(filePath);
        var videoCache = new VideoCache
        {
            FileName = fileName,
            Size = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc
        };
        
        var existingCache = CachedAssets.GetOrAdd(videoCache.FileName, videoCache);
        existingCache.Size = fileInfo.Length;
        existingCache.LastModified = fileInfo.LastWriteTimeUtc;
        
        TryFlushCache();
    }
    
    private static long GetCacheSize()
    {
        var totalSize = 0L;
        foreach (var cache in CachedAssets)
        {
            totalSize += cache.Value.Size;
        }
        
        return totalSize;
    }
}