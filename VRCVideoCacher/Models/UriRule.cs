using System.Text.Json.Serialization;

namespace VRCVideoCacher.Models;

public enum RuleAction
{
    Direct,
    Cache,
    Redirect,
    Block
}

public class UriRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public RuleAction Action { get; set; } = RuleAction.Cache;

    // Cache action options
    public int? MaxResolution { get; set; } // e.g. 1080
    public int? MaxDurationMinutes { get; set; } // e.g. 120

    // Redirect action option
    public string RedirectTarget { get; set; } = string.Empty;

    public UriRule Clone()
    {
        return new UriRule
        {
            Id = Id,
            Enabled = Enabled,
            Name = Name,
            Pattern = Pattern,
            Action = Action,
            MaxResolution = MaxResolution,
            MaxDurationMinutes = MaxDurationMinutes,
            RedirectTarget = RedirectTarget
        };
    }

    public string GetActionSummary()
    {
        switch (Action)
        {
            case RuleAction.Direct:
                return "Direct";

            case RuleAction.Cache:
                var parts = new List<string>();
                if (MaxResolution.HasValue && MaxResolution.Value > 0)
                    parts.Add($"<{MaxResolution.Value}p");
                if (MaxDurationMinutes.HasValue && MaxDurationMinutes.Value > 0)
                    parts.Add($"<{MaxDurationMinutes.Value}m");
                if (parts.Count > 0)
                    return $"Cache if {string.Join(", ", parts)}";
                return "Cache";

            case RuleAction.Redirect:
                return string.IsNullOrWhiteSpace(RedirectTarget)
                    ? "Redirect"
                    : $"Redirect to {RedirectTarget}";

            case RuleAction.Block:
                return "Block";

            default:
                return Action.ToString();
        }
    }
}
