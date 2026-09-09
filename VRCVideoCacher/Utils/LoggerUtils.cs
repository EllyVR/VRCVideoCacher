using System.Diagnostics;
using Sentry.Serilog;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Templates;
using Serilog.Templates.Themes;
using Tmds.DBus.Protocol;
using VRCVideoCacher.Services;

namespace VRCVideoCacher.Utils;

public static class LoggerUtils
{
    private const string SentryDsn = "https://233e3c027a6239500a4bb3ba81f99ddd@sentry.ellyvr.dev/19";
    private static readonly string LogsPath = Path.Join(Program.DataPath, "Logs");
    private static DateTime? LoggerStartDateTime;

    /// <summary>
    /// Controls the live minimum log level for every sink (console, file, UI). Defaults to Information so
    /// the Debug/trace output never spams the log or the log file; the LogViewer's Debug toggle flips this
    /// to <see cref="LogEventLevel.Debug"/> at runtime to bring it back.
    /// </summary>
    public static readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);

    public static void InitializeLogger()
    {
        if (LaunchArgs.ErrorReporting)
        {
            SentrySdk.Init(GetSentryOptions());
        }

        LoggerStartDateTime = DateTime.Now;
        var loggerConfiguration = new LoggerConfiguration()
            // Information and above by default; the LogViewer's Debug toggle lowers LevelSwitch at runtime.
            .MinimumLevel.ControlledBy(LevelSwitch)
            .WriteTo.Console(new ExpressionTemplate(
                "[{@t:HH:mm:ss} {@l:u3} {Coalesce(Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1),'<none>')}] {@m}" + Environment.NewLine + "{@x}",
                theme: TemplateTheme.Literate))
            .WriteTo.File(
                path: Path.Combine(LogsPath, "VRCVideoCacher.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 5);

        if (LaunchArgs.ErrorReporting)
        {
            loggerConfiguration = loggerConfiguration.WriteTo.Sentry(ConfigureSentryOptions);
        }

        if (LaunchArgs.HasGui)
        {
            loggerConfiguration = loggerConfiguration.WriteTo.Sink(new UiLogSink());
        }

        Log.Logger = loggerConfiguration.CreateLogger();
    }

    public static void LogUnhandledException(Exception ex, string message)
    {
        // Missing Linux desktop services should not trigger crash reporting
        IReadOnlyCollection<Exception> exceptions = ex is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions
            : new[] { ex };
        // Require every leaf to match so mixed aggregates still report unexpected errors.
        if (OperatingSystem.IsLinux() && LaunchArgs.HasGui && exceptions.Count > 0 &&
            exceptions.All(exception => exception is DBusErrorReplyException
            {
                ErrorName: "org.freedesktop.DBus.Error.ServiceUnknown"
            }))
        {
            try
            {
                Program.Logger.Warning(ex,
                    "A Linux desktop D-Bus service is unavailable; some desktop integration may not work");
            }
            catch
            {
            }

            return;
        }

        try
        {
            Console.WriteLine($"{message}: " + ex);
        }
        catch
        {
        }

        try
        {
            if (LaunchArgs.ErrorReporting)
            {
                SentrySdk.ConfigureScope(scope =>
                {
                    var configPath = Path.Join(Program.DataPath, "Config.json");
                    if (File.Exists(configPath))
                        scope.AddAttachment(configPath);
                });
                SentrySdk.CaptureException(ex);
            }
        }
        catch
        {
        }

        try
        {
            Program.Logger.Error(ex, "{Message}", message);

            var logFile = Path.Combine(LogsPath, $"VRCVideoCacher{(LoggerStartDateTime ?? DateTime.Now):yyyyMMdd}.log");
            if (OperatingSystem.IsWindows())
            {
                if (File.Exists(logFile))
                {
                    Process.Start("explorer.exe", $"/select,\"{logFile}\"");
                }
                else
                {
                    Process.Start("explorer.exe", LogsPath);
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", LogsPath);
            }
        }
        catch
        {
        }
    }


    private static void ConfigureSentryOptions(SentrySerilogOptions o)
    {
        SentrySdk.SetTag("noGui", LaunchArgs.HasGui.ToString());
        SentrySdk.SetTag("globalPath", LaunchArgs.UseGlobalPath.ToString());
        o.Dsn = SentryDsn;
        o.AutoSessionTracking = true;
        o.IsGlobalModeEnabled = true;
        o.Release = Program.Version;
        var platform = OperatingSystem.IsLinux() ? "linux" : "windows";
#if STEAMRELEASE
        o.Environment = $"steam-{platform}";
#else
        o.Environment = platform;
#endif
        o.EnableLogs = true;
    }

    public static SentrySerilogOptions GetSentryOptions()
    {
        var options = new SentrySerilogOptions();
        ConfigureSentryOptions(options);
        return options;
    }

}
