using Microsoft.EntityFrameworkCore;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;
using VRCVideoCacher.ViewModels;

namespace VRCVideoCacher.Database;

public static class DatabaseManager
{
    public static readonly Database Database = new();

    public static event Action? OnPlayHistoryAdded;
    public static event Action? OnVideoInfoCacheUpdated;

    static DatabaseManager()
    {
        Database.Database.EnsureCreated();
    }

    public static void Init()
    {
        Database.SaveChanges();
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
        Database.PlayHistory.Add(history);
        Database.SaveChanges();
        OnPlayHistoryAdded?.Invoke();
    }

    public static void AddVideoInfoCache(VideoInfoCache videoInfoCache)
    {
        if (string.IsNullOrEmpty(videoInfoCache.Id))
            return;

        var existingCache = Database.VideoInfoCache.Find(videoInfoCache.Id);
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
            Database.VideoInfoCache.Add(videoInfoCache);
        }
        Database.SaveChanges();
        OnVideoInfoCacheUpdated?.Invoke();
    }

    public static List<History> GetPlayHistory(int limit = 50)
    {
        return Database.PlayHistory
            .AsNoTracking()
            .OrderByDescending(h => h.Timestamp)
            .Take(limit)
            .ToList();
    }

    public static IEnumerable<HistoryItemViewModel> GetVideoHistoryAsCache(int limit = 50)
    {
        return Database.PlayHistory
            .AsNoTracking()
            .OrderByDescending(h => h.Timestamp)
            .Take(limit)
            .LeftJoin(Database.VideoInfoCache,
                h => h.Id,
                v => v.Id,
                (h, v) => new HistoryItemViewModel(h, v))
            .ToList()
            .DistinctBy(h => h.Url);
    }
}