using System.ComponentModel.DataAnnotations;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Database.Models;

public class History
{
    [Key]
    public int Key { get; set; }
    public required DateTime Timestamp { get; set; }
    public required string Url { get; set; }
    public required string? Id { get; set; }
    public required UrlType Type { get; set; }
}