using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;
using VRCVideoCacher.Models;

namespace VRCVideoCacher.Services;

public static class LogService
{
    public static event Action<LogEntry>? OnLogEntry;

    // Buffer to store logs before UI subscribes
    private static readonly ConcurrentQueue<LogEntry> LogBuffer = new();
    private const int MaxBufferSize = 500;

    public static void EmitLogEntry(LogEvent logEvent)
    {
        var level = logEvent.Level switch
        {
            LogEventLevel.Verbose => "VRB",
            LogEventLevel.Debug => "DBG",
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            LogEventLevel.Error => "ERR",
            LogEventLevel.Fatal => "FTL",
            _ => "???"
        };

        var source = "Unknown";
        if (logEvent.Properties.TryGetValue("SourceContext", out var sourceContext))
        {
            var sourceStr = sourceContext.ToString().Trim('"');
            var lastDot = sourceStr.LastIndexOf('.');
            source = lastDot >= 0 ? sourceStr[(lastDot + 1)..] : sourceStr;
        }

        var entry = new LogEntry
        {
            Timestamp = logEvent.Timestamp.DateTime,
            Level = level,
            Source = source,
            Message = logEvent.RenderMessage()
        };

        // Add to buffer
        LogBuffer.Enqueue(entry);
        while (LogBuffer.Count > MaxBufferSize)
            LogBuffer.TryDequeue(out _);

        // Emit to subscribers
        OnLogEntry?.Invoke(entry);
    }

    // Get all buffered logs (for UI initialization)
    public static IEnumerable<LogEntry> GetBufferedLogs() => LogBuffer.ToArray();
}

public class UiLogSink : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        LogService.EmitLogEntry(logEvent);
    }
}
