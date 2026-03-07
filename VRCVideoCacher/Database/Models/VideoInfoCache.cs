using System.ComponentModel.DataAnnotations;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Database.Models;

public class VideoInfoCache
{
    [Key]
    public required string Id { get; set; }
    public string? Title { get; set; }
    public string? Author { get; set; }
    public double? Duration { get; set; }
    public UrlType Type { get; set; }
}