namespace VRCVideoCacher.Utils;

public class LaunchArgs
{
    private const string AdminBypassArg = "--bypass-admin-warning";
    private const string NoGuiArg = "--no-gui";
    private const string DisableErrorReportingArg = "--disable-error-reporting";
    private const string GlobalPathArg = "--global-path";
    private const string OldPidArg = "--old-pid";
    private const string KillExistingInstanceArg = "--kill-existing-instance";
    private const string NoSteamArg = "--no-steam";
    private const string NoOvrArg = "--no-ovr";
    private const string CloseWithSteamVrArg = "--close-with-steamvr";

    public static bool IsBypassArgumentPresent;
    public static bool HasGui = true;
    public static bool ErrorReporting = true;
    public static bool UseGlobalPath;
    public static int? OldPid;
    public static bool KillExistingInstance = false;
    public static bool SteamSdk = true;
    public static bool OVR = true;
    public static bool CloseWithSteamVr = false;

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

            if (arg.StartsWith(OldPidArg, StringComparison.OrdinalIgnoreCase))
            {
                var pidStr = arg.Substring(OldPidArg.Length + 1);
                if (int.TryParse(pidStr, out var pid))
                    OldPid = pid;
            }

            if (arg.Equals(KillExistingInstanceArg, StringComparison.OrdinalIgnoreCase))
                KillExistingInstance = true;

            if (arg.Equals(NoSteamArg, StringComparison.OrdinalIgnoreCase))
                SteamSdk = false;

            if (arg.Equals(NoOvrArg, StringComparison.OrdinalIgnoreCase))
                OVR = false;

            if (arg.Equals(CloseWithSteamVrArg, StringComparison.OrdinalIgnoreCase))
                CloseWithSteamVr = true;
        }
    }

    public static List<string> BuildArgs()
    {
        var args = new List<string>();
        if (IsBypassArgumentPresent)
            args.Add(AdminBypassArg);

        if (!HasGui)
            args.Add(NoGuiArg);

        if (!ErrorReporting)
            args.Add(DisableErrorReportingArg);

        if (UseGlobalPath)
            args.Add(GlobalPathArg);

        return args;
    }
}