namespace VRCVideoCacher.Services.Nico;

public class NicoVideoResult
{
    public string? VideoId { get; set; }
    public string? Url { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string[]? Tags { get; set; }
    public long? ViewCount { get; set; }
    public long? CommentCount { get; set; }
    public long? MyListCount { get; set; }
    public long? LikeCount { get; set; }
    public long? Duration { get; set; }
    public string? Thumbnail { get; set; }
    public string? StreamUrl { get; set; }
    public Dictionary<string, string> Cookies { get; set; } = new();
}