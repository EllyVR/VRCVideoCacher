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
        var indexPath = Path.Combine(CacheManager.CachePath, "index.html");
        if (!File.Exists(indexPath))
            File.WriteAllText(indexPath, "VRCVideoCacher");
        
        _server = CreateWebServer(ConfigManager.Config.ytdlWebServerURL);
        _server.RunAsync();  
    }
    
    private static EmbedIO.WebServer CreateWebServer(string url)
    {
        try { Logger.UnregisterLogger<ConsoleLogger>(); } catch { /* Not registered */ }
        Logger.RegisterLogger<WebServerLogger>();

        var urls = new List<string>
        {
            "http://localhost:9696",
            "http://127.0.0.1:9696"
        };
        if (!urls.Contains(url))
            urls.Add(url);
        
        var server = new EmbedIO.WebServer(o => o
                .WithUrlPrefixes(urls)
                .WithMode(HttpListenerMode.EmbedIO))
            // First, we will configure our web server by adding Modules.
            .WithWebApi("/api", m => m
                .WithController<ApiController>())
            .WithStaticFolder("/", CacheManager.CachePath, true, m => m
                .WithContentCaching(true));

        // Listen for state changes.
        server.StateChanged += (_, e) => $"WebServer State: {e.NewState}".Info();
        server.OnUnhandledException += OnUnhandledException;
        server.OnHttpException += OnHttpException;  
        return server;
    }

    private static Task OnHttpException(IHttpContext context, IHttpException httpException)
    {
        Log.Information(httpException.Message!);
        return Task.CompletedTask;
    }

    private static Task OnUnhandledException(IHttpContext context, Exception exception)
    {
        Log.Information(exception.Message);
        return Task.CompletedTask;
    }
}