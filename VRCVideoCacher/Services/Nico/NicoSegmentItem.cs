namespace VRCVideoCacher.Services.Nico;

internal sealed class NicoSegmentItem
{
    public string Url { get; init; } = "";
    public double Duration { get; init; }
    public string? KeyUrl { get; init; }
    public string? KeyIv { get; init; }
}