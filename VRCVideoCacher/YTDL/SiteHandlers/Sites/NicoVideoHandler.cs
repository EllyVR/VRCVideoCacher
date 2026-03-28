using System.Text.RegularExpressions;
using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

public class NicoVideoHandler : ISiteHandler
{
    
    private static readonly ILogger Log = Program.Logger.ForContext<NicoVideoHandler>();

    // Matches full nicovideo/niconico URLs
    private static readonly Regex NicoID1 = new(@"^(https?)://(live|www)\.nicovideo\.jp/watch/(.+)$", RegexOptions.Compiled);
    private static readonly Regex NicoID2 = new(@"^(https?)://nico\.ms/(.+)$", RegexOptions.Compiled);

    // Matches bare Nico video/live IDs
    private static readonly Regex NicoID4 = new(@"^(sm\d+|nm\d+|am\d+|fz\d+|ut\d+|dm\d+|so\d+|ax\d+|ca\d+|cd\d+|cw\d+|fx\d+|ig\d+|na\d+|om\d+|sd\d+|sk\d+|yk\d+|yo\d+|za\d+|zb\d+|zc\d+|zd\d+|ze\d+|nl\d+|ch\d+|\d+|lv\d+)$", RegexOptions.Compiled);

    public bool CanHandle(Uri uri) => false; // rewrite only, GenericHandler picks up after

    public Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro) => Task.FromResult<VideoInfo?>(null);
    
    public Task<string> RewriteUrl(string url, Uri uri)
    {
        if (!uri.Host.EndsWith("nicovideo.jp") && !uri.Host.EndsWith("nico.ms"))
            return Task.FromResult(url);

        var (m, group) = new[]
        {
            (NicoID1.Match(url), 3),
            (NicoID2.Match(url), 2),
            (NicoID4.Match(url), 1),
        }.FirstOrDefault(x => x.Item1.Success);

        if (m?.Success != true)
            return Task.FromResult(url);

        var newUrl = $"https://www.nicovideo.life/watch/{m.Groups[group].Value}";
        Log.Information("Incompatible URL, passing to external resolver: {URL}", newUrl);
        return Task.FromResult(newUrl);
    }
    
}