using Avalonia;
using Serilog;
using Serilog.Templates;
using Serilog.Templates.Themes;
using VRCVideoCacher.UI.Services;

namespace VRCVideoCacher.UI;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Check for --nogui flag
        if (args.Contains("--nogui"))
        {
            // Run backend only (console mode)
            VRCVideoCacher.Program.Main(args).GetAwaiter().GetResult();
            return;
        }

        // Configure Serilog with UI sink
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(new ExpressionTemplate(
                "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}\n{@x}",
                theme: TemplateTheme.Literate))
            .WriteTo.Sink(new UiLogSink())
            .CreateLogger();

        // Start backend on background thread
        Task.Run(async () =>
        {
            try
            {
                await VRCVideoCacher.Program.Main(args);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Backend error "+ex.Message+" "+ex.StackTrace);
            }
        });

        // Give the backend a moment to initialize
        Thread.Sleep(1000);

        // Start the UI
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
