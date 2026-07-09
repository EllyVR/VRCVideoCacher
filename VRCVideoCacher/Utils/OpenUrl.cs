using System.Diagnostics;
using Serilog;

namespace VRCVideoCacher.Utils;

public static class OpenUrl
{
    private static readonly ILogger Log = Program.Logger.ForContext("SourceContext", nameof(OpenUrl));

    public static bool Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Log.Warning("Refused to open empty URL");
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            Log.Warning("Refused to open invalid URL {Url}", url);
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp &&
            uri.Scheme != Uri.UriSchemeHttps)
        {
            Log.Warning("Refused to open non-web URL {Url}", url);
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            };
            return Process.Start(psi) != null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open link: {Url}", url);
            return false;
        }
    }
}