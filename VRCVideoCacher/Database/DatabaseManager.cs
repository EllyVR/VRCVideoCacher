using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;
using VRCVideoCacher.ViewModels;

namespace VRCVideoCacher.Database;

public static class DatabaseManager
{
    public static event Action? OnPlayHistoryAdded;
    public static event Action? OnVideoInfoCacheUpdated;
    
    private static readonly PooledDbContextFactory<Database> _contextFactory;

    static DatabaseManager()
    {
        Directory.CreateDirectory(Database.CacheDir);

        var options = new DbContextOptionsBuilder<Database>()
            .UseSqlite($"Data Source={Database.DbPath}")
            .EnableSensitiveDataLogging()
            .Options;

        _contextFactory = new PooledDbContextFactory<Database>(options);

        using var db = _contextFactory.CreateDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }
    
    public static void AddPlayHistory(VideoInfo videoInfo)
    {
        var history = new History
        {
            Timestamp = DateTime.UtcNow,
            Url = videoInfo.VideoUrl,
            Id = videoInfo.VideoId,
            Type = videoInfo.UrlType
        };
        using var db = _contextFactory.CreateDbContext();
        db.PlayHistory.Add(history);
        db.SaveChanges();
        OnPlayHistoryAdded?.Invoke();
    }

    public static void AddVideoInfoCache(VideoInfoCache videoInfoCache)
    {
        if (string.IsNullOrEmpty(videoInfoCache.Id))
            return;

        using var db = _contextFactory.CreateDbContext();
        var existingCache = db.VideoInfoCache.Find(videoInfoCache.Id);
        if (existingCache != null)
        {
            if (string.IsNullOrEmpty(existingCache.Title) &&
                !string.IsNullOrEmpty(videoInfoCache.Title))
                existingCache.Title = videoInfoCache.Title;

            if (string.IsNullOrEmpty(existingCache.Author) &&
                !string.IsNullOrEmpty(videoInfoCache.Author))
                existingCache.Author = videoInfoCache.Author;

            if (existingCache.Duration == null &&
                videoInfoCache.Duration != null)
                existingCache.Duration = videoInfoCache.Duration;
        }
        else
        {
            db.VideoInfoCache.Add(videoInfoCache);
        }
        db.SaveChanges();
        OnVideoInfoCacheUpdated?.Invoke();
    }

    public static List<History> GetPlayHistory(int limit = 50)
    {
        using var db = _contextFactory.CreateDbContext();
        return db.PlayHistory
            .AsNoTracking()
            .OrderByDescending(h => h.Timestamp)
            .Take(limit)
            .ToList();
    }

    public static IEnumerable<HistoryItemViewModel> GetVideoHistoryAsCache(int limit = 50)
    {
        using var db = _contextFactory.CreateDbContext();
        return db.PlayHistory
            .AsNoTracking()
            .OrderByDescending(h => h.Timestamp)
            .Take(limit)
            .LeftJoin(db.VideoInfoCache,
                h => h.Id,
                v => v.Id,
                (h, v) => new HistoryItemViewModel(h, v))
            .ToList()
            .DistinctBy(h => h.Url);
    }

    public static VideoInfoCache? GetVideoInfoCache(string videoId)
    {
        using var db = _contextFactory.CreateDbContext();
        return db.VideoInfoCache.Find(videoId);
    }
    
}