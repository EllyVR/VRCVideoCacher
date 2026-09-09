using System.Diagnostics;
using System.Runtime.CompilerServices;
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
    private static int _desktopServiceNoticeLogged;

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

    internal static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        ThreadPool.QueueUserWorkItem(static (AggregateException exception) =>
        {
            try
            {
                LogUnhandledException(exception, "Unobserved task exception");
            }
            catch
            {
                // Never turn a failure in exception reporting into another unhandled exception.
            }
        }, e.Exception, preferLocal: false);
    }

    public static void LogUnhandledException(Exception ex, string message)
    {
        if (OperatingSystem.IsLinux() && LaunchArgs.HasGui && IsUnavailableDesktopServiceException(ex))
        {
            if (Interlocked.Exchange(ref _desktopServiceNoticeLogged, 1) == 0)
            {
                try
                {
                    Program.Logger.Information(
                        "A Linux desktop D-Bus service is unavailable; some desktop integration may not work");
                }
                catch
                {
                }
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

    // Keep D-Bus type resolution and aggregate traversal out of non-Linux/headless handling.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool IsUnavailableDesktopServiceException(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            var exceptions = aggregate.Flatten().InnerExceptions;
            if (exceptions.Count == 0)
                return false;

            foreach (var inner in exceptions)
            {
                if (inner is not DBusErrorReplyException
                    { ErrorName: "org.freedesktop.DBus.Error.ServiceUnknown" })
                    return false;
            }

            return true;
        }

        return exception is DBusErrorReplyException
            { ErrorName: "org.freedesktop.DBus.Error.ServiceUnknown" };
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
