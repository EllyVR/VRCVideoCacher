using System.Text;
using EmbedIO;

namespace VRCVideoCacher.Services.Sabr;

/// <summary>
/// Materialises the requested SABR HLS file, then lets the static file module actually serve it
/// (<see cref="IsFinalHandler"/> is false, so routing continues).
/// </summary>
internal sealed class SabrHlsModule(string baseRoute) : WebModuleBase(baseRoute)
{
    public override bool IsFinalHandler => false;

    protected override async Task OnRequestAsync(IHttpContext context)
    {
        // A LIVE playlist is answered here rather than falling through to the static file module: that
        // module would attach an ETag and honour If-None-Match, and a playlist that changes every couple
        // of seconds must never be answered 304. Everything else (segments, init, VOD playlists) falls
        // through as before.
        if (await SabrRestreamService.TryGetLivePlaylistAsync(context.RequestedPath) is { } playlist)
        {
            context.Response.ContentType = "application/vnd.apple.mpegurl";
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            await context.SendStringAsync(playlist, "application/vnd.apple.mpegurl", Encoding.UTF8);
            context.SetHandled();
            return;
        }

        await SabrRestreamService.EnsureAsync(context.RequestedPath);
    }
}