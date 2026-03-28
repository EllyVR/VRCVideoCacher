namespace VRCVideoCacher.Utils;

public class HttpUtil
{
    internal static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } }
    };
}