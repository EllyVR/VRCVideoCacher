using EmbedIO;

namespace VRCVideoCacher.Services.Nico;

/// <summary>
///     Materialises the requested NicoNico HLS file, then lets the static file module actually serve it
///     (<see cref="IsFinalHandler" /> is false, so routing continues), and handles temp video streaming.
/// </summary>
internal sealed class NicoHlsModule(string baseRoute) : WebModuleBase(baseRoute)
{
    public override bool IsFinalHandler => false;

    protected override async Task OnRequestAsync(IHttpContext context)
    {
        var path = context.RequestedPath.TrimStart('/');
        if (path.StartsWith("temp/", StringComparison.OrdinalIgnoreCase))
        {
            var tempFileName = path[5..];
            await NicoRestreamService.HandleTempVideoAsync(context, tempFileName);
            return;
        }

        var slash = path.IndexOf('/');
        if (slash < 0) return;
        var videoId = path[..slash];
        var fileName = path[(slash + 1)..];
        if (string.IsNullOrEmpty(videoId) || string.IsNullOrEmpty(fileName)) return;

        await NicoRestreamService.EnsureAsync(videoId, fileName);
    }
}