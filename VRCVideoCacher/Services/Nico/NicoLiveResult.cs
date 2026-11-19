namespace VRCVideoCacher.Services.Nico;

public class NicoLiveResult
{
    public string? LiveId { get; set; }
    public string? Url { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string[]? Tags { get; set; }
    public long? ViewCount { get; set; }
    public long? CommentCount { get; set; }
    public string? Thumbnail { get; set; }
    public string? Status { get; set; }
    public string? WebSocketUrl { get; set; }
    public int? FrontendId { get; set; }
    public Dictionary<string, string> Cookies { get; set; } = new();
}