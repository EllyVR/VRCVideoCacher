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

    public static IEnumerable<HistoryItemViewModel> GetVideoHistoryAsCache(int limit = 50, bool distinctOnly = false)
    {
        using var db = _contextFactory.CreateDbContext();
        var query = db.PlayHistory
            .AsNoTracking()
            .OrderByDescending(h => h.Timestamp);

        var historyItems = distinctOnly
            ? query.GroupBy(h => h.Id)
                   .Select(g => g.First())
                   .Take(limit)
            : query.Take(limit);

        // Materialize the database query first
        var histories = historyItems.ToList();

        // Fetch matching VideoInfoCache entries
        var ids = histories.Select(h => h.Id).Where(id => id != null).Distinct().ToList();
        var cacheDict = db.VideoInfoCache
            .AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .ToDictionary(v => v.Id);

        // Project to ViewModel in-memory
        return histories.Select(h => 
        {
            cacheDict.TryGetValue(h.Id ?? string.Empty, out var meta);
            return new HistoryItemViewModel(h, meta);
        }).ToList();
    }

    public static VideoInfoCache? GetVideoInfoCache(string videoId)
    {
        using var db = _contextFactory.CreateDbContext();
        return db.VideoInfoCache.Find(videoId);
    }
    
}