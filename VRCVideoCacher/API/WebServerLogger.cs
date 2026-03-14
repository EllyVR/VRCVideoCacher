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
        switch (logEvent.MessageType)
        {
            case LogLevel.Error:
                WebServer.Log.Error("{WebServerLogEvent}", logEvent.Message);
                break;
            case LogLevel.Warning:
                WebServer.Log.Warning("{WebServerLogEvent}", logEvent.Message);
                break;
            case LogLevel.Info:
                WebServer.Log.Information("{WebServerLogEvent}", logEvent.Message);
                break;
        }
    }
}