using Avalonia.Media;
// ReSharper disable ReplaceWithFieldKeyword

namespace VRCVideoCacher.Models;

public class LogEntry
{
    public DateTime Timestamp { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    private readonly Color _errorColor = Color.Parse("#CF6679");
    private readonly Color _warnColor = Color.Parse("#FFB74D");
    private readonly Color _infoColor = Color.Parse("#81C784");
    private readonly Color _debugColor = Color.Parse("#64B5F6");
    private readonly Color _stdColor = Color.Parse("#FFFFFF");

    public Color LevelColor => Level switch
    {
        "ERR" or "FTL" => _errorColor,
        "WRN" => _warnColor,
        "INF" => _infoColor,
        "DBG" => _debugColor,
        _ => _stdColor
    };
}
