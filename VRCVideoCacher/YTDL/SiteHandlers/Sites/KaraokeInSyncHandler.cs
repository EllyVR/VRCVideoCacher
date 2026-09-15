using Avalonia.Controls;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class KaraokeInSyncHandler : ISiteHandler
{
    private static readonly ILogger Log = Program.Logger.ForContext<KaraokeInSyncHandler>();

    public bool CanHandle(Uri uri) => false; // rewrite only

    public Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro) => Task.FromResult<VideoInfo?>(null);

    public Task<string> RewriteUrl(string url, Uri uri)
    {
        if (!url.StartsWith("https://ksync.arcanescripts.com/custom/redir-url"))
            return Task.FromResult(url);

        string[] splitQuery = uri.Query.Split(":", 2);

        if (!url.Contains("Paste the YouTube Link after the colon:") || splitQuery.Length != 2)
        {
            Log.Warning("Unknown Karaoke in Sync Custom URL {URL} detected, passing through");
            return Task.FromResult(url);
        }

        var newUrl = splitQuery[1];
        Log.Information("Karaoke in Sync Custom URL detected, stripped to: {URL}", newUrl);
        return Task.FromResult(newUrl);
    }

    public List<string> GetYtdlpArguments(Uri uri, bool avPro) => [];

}