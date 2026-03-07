namespace VRCVideoCacher.Utils;

public static class AdminCheck
{
    private const string AdminTitleWarning = " - RUNNING AS AN ADMINISTRATOR!";
    public const string AdminBypassArg = "--bypass-admin-warning";
    public const string AdminWarningMessage =
        "⚠ WARNING: You are running VRCVideoCacher as an administrator. " +
        "This is not recommended for security reasons. " +
        "Please run the application with standard user privileges. " +
        $"\r\n\r\nIf you really need it, please use \"{AdminBypassArg}\" to bypass this warning.";

    private static bool _isBypassArguementPresent;

    public static void SetupArguements(params string[] args)
    {
        _isBypassArguementPresent = false;

        foreach (var arg in args)
        {
            if (arg.Equals(AdminBypassArg, StringComparison.OrdinalIgnoreCase))
            {
                _isBypassArguementPresent = true;
                return;
            }
        }
    }

    public static bool ShouldShowAdminWarning()
    {
        return IsRunningAsAdmin() && !_isBypassArguementPresent;
    }

    public static string GetAdminTitleWarning()
    {
        if (IsRunningAsAdmin())
        {
            return AdminTitleWarning;
        }
        return string.Empty;
    }

    public static bool IsRunningAsAdmin()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return Environment.UserName == "root";
        }
        return false;
    }
}
