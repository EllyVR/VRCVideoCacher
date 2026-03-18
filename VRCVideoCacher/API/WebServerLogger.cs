using Swan.Logging;
// ReSharper disable ClassNeverInstantiated.Global

namespace VRCVideoCacher.API;

public class WebServerLogger : ILogger
{
    public LogLevel LogLevel { get; } = LogLevel.Info;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public void Log(LogMessageReceivedEventArgs logEvent)
    {
        var trace = logEvent.Exception != null ? logEvent.Exception.ToString() : string.Empty;
        var message = string.IsNullOrEmpty(trace) ? logEvent.Message : $"{logEvent.Message}\n{trace}";
        switch (logEvent.MessageType)
        {
            case LogLevel.Error:
                WebServer.Log.Error("{WebServerLogEvent}", message);
                break;
            case LogLevel.Warning:
                WebServer.Log.Warning("{WebServerLogEvent}", message);
                break;
            case LogLevel.Info:
                WebServer.Log.Information("{WebServerLogEvent}", message);
                break;
        }
    }
}