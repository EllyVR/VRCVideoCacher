using System.Web;
using Serilog;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class PyPyDanceHandler : ISiteHandler
{
    private static readonly ILogger Log = Program.Logger.ForContext<PyPyDanceHandler>();
    private static readonly string[] Prefixes = ["http://api.pypy.dance/video", "https://api.pypy.dance/video"];
    
    public bool CanHandle(Uri uri) => Prefixes.Any(p => uri.ToString().StartsWith(p));
    
    public async Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var result = await HttpUtil.HttpClient.SendAsync(request);
            var videoUrl = result.RequestMessage?.RequestUri?.ToString();
            if (string.IsNullOrEmpty(videoUrl))
            {
                Log.Error("Failed to get video ID from PypyDance URL: {URL} Response: {Response} - {Data}", url, result.StatusCode, await result.Content.ReadAsStringAsync());
                return null;
            }

            var finalUri = new Uri(videoUrl);
            var fileName = Path.GetFileName(finalUri.LocalPath);
            var videoId = !fileName.Contains('.') ? fileName : fileName.Split('.')[0];

            var query = HttpUtility.ParseQueryString(uri.Query);
            var success = int.TryParse(query.Get("id"), out var idInt);
            if (!success)
            {
                Log.Error("Failed to get video ID from PypyDance URL: {URL}", url);
                return null;
            }

            _ = Task.Run(async () => await PyPyDanceApiService.DownloadMetadata(idInt, videoId));

            return new VideoInfo
            {
                VideoUrl = videoUrl,
                VideoId = videoId,
                UrlType = UrlType.PyPyDance,
                DownloadFormat = DownloadFormat.MP4
            };
        }
        catch
        {
            Log.Error("Failed to get video ID from PypyDance URL: {URL}", url);
            return null;
        }
    }
    
}