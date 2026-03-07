using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Services;

internal class PyPyDanceBundle
{
    [JsonPropertyName("songs")]
    public List<PyPyDanceSong>? Songs { get; set; }
}

public class PyPyDanceSong
{
    [JsonPropertyName("i")]
    public int? Id { get; set; }
    
    [JsonPropertyName("n")]
    public string? Name { get; set; }
    
    [JsonPropertyName("s")]
    public int? StartTime { get; set; }
    
    [JsonPropertyName("e")]
    public int? EndTime { get; set; }
}
[JsonSerializable(typeof(PyPyDanceBundle))]
internal partial class PyPyDanceBundleContext : JsonSerializerContext
{
}

public class PyPyDanceApiService
{
    private const string PyPyDanceApiUrl = "https://api.pypy.dance/bundle";
    private static readonly ILogger Logger = Program.Logger.ForContext<VRDancingAPIService>();
    private static DateTime _lastFetch = DateTime.MinValue;
    private static List<PyPyDanceSong> _songs = [];
    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", $"VRCVideoCacher {Program.Version}" } },
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static async Task<PyPyDanceSong?> GetVideoInfo(int? videoId)
    {
        if (videoId == 0  || videoId == null)
            return null;

        try
        {
            if ((DateTime.Now - _lastFetch).TotalMinutes > 60)
                await FetchBundle();

            return _songs.Find(song => song.Id == videoId);
        }
        catch
        {
            return null;
        }
    }

    private static async Task FetchBundle()
    {
        _lastFetch = DateTime.Now;
        var req = await HttpClient.GetStringAsync(PyPyDanceApiUrl);
        var bundle = JsonSerializer.Deserialize(req, PyPyDanceBundleContext.Default.PyPyDanceBundle);
        if (bundle?.Songs != null)
            _songs = bundle.Songs;
    }
    
    public static async Task DownloadMetadata(int idInt, string videoId)
    {
        try
        {
            var thumbnailUrl = $"https://api.pypy.dance/thumb?id={idInt}";
            await ThumbnailManager.TrySaveThumbnail(videoId, thumbnailUrl);
            
            var songInfo = await GetVideoInfo(idInt);
            int? duration = null;
            if (songInfo?.EndTime != null)
                duration = songInfo.EndTime;
            if (songInfo?.StartTime != null && duration != null)
                duration -= songInfo.StartTime;
            
            DatabaseManager.AddVideoInfoCache(new VideoInfoCache
            {
                Id = videoId,
                Title = songInfo?.Name,
                Author = null,
                Duration = duration,
                Type = UrlType.PyPyDance
            });
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to download video metadata: {Ex}", ex.Message);
        }
    }
}