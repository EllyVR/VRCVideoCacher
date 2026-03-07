using Newtonsoft.Json;
using Serilog;
using VRCVideoCacher.Database;
using VRCVideoCacher.Database.Models;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Services;

public class VRDancingAPIService
{
    private const string VRDancingAPIBaseURL = "https://dbapi.vrdancing.club/";
    private static readonly ILogger Logger = Program.Logger.ForContext<VRDancingAPIService>();
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri(VRDancingAPIBaseURL),
        DefaultRequestHeaders = { { "User-Agent", $"VRCVideoCacher {Program.Version}" } },
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static async Task<VRDSongInfo?> GetVideoInfo(string code)
    {
        var req = await HttpClient.GetAsync($"/api/v1/public/getsong?code={code}");
        var str = await req.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<VRDSongInfo>(str);
    }
    
    public static async Task DownloadMetadata(string code, string videoId)
    {
        try
        {
            var vrdData = await GetVideoInfo(code);
            if (vrdData == null)
                return;
            
            await ThumbnailManager.TrySaveThumbnail(videoId, vrdData.ThumbnailURL);
            DatabaseManager.AddVideoInfoCache(new VideoInfoCache
            {
                Id = videoId,
                Title = vrdData.Song,
                Author = vrdData.Artist,
                Type = UrlType.VRDancing
            });
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to download video metadata: {Ex}", ex.Message);
        }
    }
}

public class VRDSongInfo
{
    public string Artist;
    public string Song;
    public string Instructor;
    public string ThumbnailURL;
    public string Hash;
}