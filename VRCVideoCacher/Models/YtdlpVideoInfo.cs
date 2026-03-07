using System.Text.Json.Serialization;

namespace VRCVideoCacher.Models;

internal class YtdlpVideoInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    [JsonPropertyName("is_live")]
    public bool? IsLive { get; set; }
    
    [JsonPropertyName("title")]
    public string? Name { get; set; }
    
    [JsonPropertyName("author_name")]
    public string? Author { get; set; }
}

[JsonSerializable(typeof(YtdlpVideoInfo))]
internal partial class VideoIdJsonContext : JsonSerializerContext
{
}