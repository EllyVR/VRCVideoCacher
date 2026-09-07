using Serilog;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.YTDL.SiteHandlers.Sites;

/// <summary>
/// Twitter / X video handler. Behaves like the generic handler (no caching, hashed id), but forces yt-dlp
/// to pick the <b>progressive, muxed</b> http(s) MP4 rather than the split HLS (m3u8) stream.
///
/// Twitter serves each video both as an HLS manifest (separate video + audio, protocol <c>m3u8_native</c>)
/// and as pre-muxed progressive MP4 variants (protocol <c>https</c>). yt-dlp's default selection tends to
/// return the m3u8, which VRChat/AVPro cannot play without restreaming — but the muxed MP4 is right there,
/// so we just ask for it. See https://github.com/EllyVR/VRCVideoCacher/issues/200
/// </summary>
public class TwitterHandler : ISiteHandler
{
    private static readonly ILogger Log = Program.Logger.ForContext<TwitterHandler>();
    private static readonly string[] Hosts =
        ["twitter.com", "www.twitter.com", "mobile.twitter.com", "x.com", "www.x.com", "mobile.x.com"];

    // Best pre-muxed http(s) format (Twitter's progressive MP4 carries both video and audio); the
    // `^=http` protocol match deliberately excludes `m3u8_native`. Falls back to best if none exists.
    private const string MuxedHttpFormat = "best[protocol^=http]/best";

    public bool CanHandle(Uri uri) => Hosts.Contains(uri.Host);

    public Task<VideoInfo?> GetVideoInfo(string url, Uri uri, bool avPro)
    {
        var videoId = VideoId.HashUrl(url);
        Log.Information("Handling Twitter/X URL: {URL}", url);
        return Task.FromResult<VideoInfo?>(new VideoInfo
        {
            VideoUrl = url,
            VideoId = videoId,
            UrlType = UrlType.Other,
            DownloadFormat = DownloadFormat.MP4
        });
    }

    public List<string> GetYtdlpArguments(Uri uri, bool avPro) =>
        [$"-f \"{MuxedHttpFormat}\""];

    public Task<string> RewriteUrl(string url, Uri uri) => Task.FromResult(url);
}
