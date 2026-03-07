using System.Text.Json;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Services;

public static class YouTubeMetadataService
{
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } },
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static async Task<VideoInfoCache?> GetVideoTitleAsync(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        try
        {
            var url = $"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={videoId}&format=json";
            var response = await HttpClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(response);
            if (!doc.RootElement.TryGetProperty("title", out var titleElement))
                return null;
            var title = titleElement.GetString();
            doc.RootElement.TryGetProperty("author_name", out var authorElement);
            var author = authorElement.GetString();
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(author))
                return null;

            var videoInfo = new VideoInfoCache
            {
                Id = videoId,
                Title = title,
                Author = author,
                Type = UrlType.YouTube
            };
            DatabaseManager.AddVideoInfoCache(videoInfo);
            return videoInfo;
        }
        catch
        {
            // Silently fail - we'll just show the video ID
        }

        return null;
    }

    public static async Task<string?> GetThumbnail(string videoId)
    {
        if (string.IsNullOrEmpty(videoId))
            return null;

        var localPath = ThumbnailManager.GetThumbnailPath(videoId);
        if (File.Exists(localPath))
            return localPath;
        
        var url = $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg";
        var thumbnailPath = await ThumbnailManager.TrySaveThumbnail(videoId, url);
        if (!string.IsNullOrEmpty(thumbnailPath))
            return thumbnailPath;

        return url;
    }
}
