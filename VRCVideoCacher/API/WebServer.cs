using EmbedIO;
using EmbedIO.Files;
using EmbedIO.WebApi;
using Swan.Logging;
using ILogger = Serilog.ILogger;

namespace VRCVideoCacher.API;

public class WebServer
{
    private static EmbedIO.WebServer? _server;
    public static readonly ILogger Log = Program.Logger.ForContext<WebServer>();
    
    public static void Init()
    {
        if (!ConfigManager.Config.ytdlWebServerURL.Contains("localhost") &&
            !ConfigManager.Config.ytdlWebServerURL.Contains("127.0.0.1"))
        {
            Log.Warning("WebServer in config isn't localhost, not starting local WebServer.");
            return;
        }
        _server = CreateWebServer(ConfigManager.Config.ytdlWebServerURL);
        _server.RunAsync();  
    }
    
    private static EmbedIO.WebServer CreateWebServer(string url)
    {
        Logger.UnregisterLogger<ConsoleLogger>();
        Logger.RegisterLogger<WebServerLogger>();
        var server = new EmbedIO.WebServer(o => o
                .WithUrlPrefix(url)
                .WithMode(HttpListenerMode.EmbedIO))
            // First, we will configure our web server by adding Modules.
            .WithWebApi("/api", m => m
                .WithController<ApiController>())
            .WithStaticFolder("/", ConfigManager.Config.CachedAssetPath, true, m => m
                .WithContentCaching(true));

        // Listen for state changes.
        server.StateChanged += (_, e) => $"WebServer State: {e.NewState}".Info();
        server.OnUnhandledException += OnUnhandledException;
        server.OnHttpException += OnHttpException;  
        return server;
    }

    private static Task OnHttpException(IHttpContext context, IHttpException httpException)
    {
        Log.Warning("HTTP {StatusCode}: {Message}", httpException.StatusCode, httpException.Message);
        return Task.CompletedTask;
    }

    private static Task OnUnhandledException(IHttpContext context, Exception exception)
    {
        Log.Error(exception, "Unhandled exception processing request {Path}", context.Request.Url?.AbsolutePath);
        return Task.CompletedTask;
    }
}