namespace VRCVideoCacher.Utils;

public class LaunchArgs
{
    private const string AdminBypassArg = "--bypass-admin-warning";
    private const string NoGuiArg = "--no-gui";
    private const string DisableErrorReportingArg = "--disable-error-reporting";
    private const string GlobalPathArg = "--global-path";

    public static bool IsBypassArgumentPresent;
    public static bool HasGui = true;
    public static bool ErrorReporting = true;
    public static bool UseGlobalPath;

    public static void SetupArguments(params string[] args)
    {
        IsBypassArgumentPresent = false;

        foreach (var arg in args)
        {
            if (arg.Equals(AdminBypassArg, StringComparison.OrdinalIgnoreCase))
                IsBypassArgumentPresent = true;

            if (arg.Equals(NoGuiArg, StringComparison.OrdinalIgnoreCase))
                HasGui = false;

            if (arg.Equals(DisableErrorReportingArg, StringComparison.OrdinalIgnoreCase))
                ErrorReporting = false;

            if (arg.Equals(GlobalPathArg, StringComparison.OrdinalIgnoreCase))
                UseGlobalPath = true;
        }
    }
}