namespace VRCVideoCacher.Utils;

public static class AdminCheck
{
    private const string AdminTitleWarning = " - RUNNING AS AN ADMINISTRATOR!";

    public const string AdminWarningMessage =
        "⚠ WARNING: You are running VRCVideoCacher as an administrator. " +
        "This is not recommended for security reasons. " +
        "Please run the application with standard user privileges. " +
        "\r\n\r\nIf you really need it, please use --bypass-admin-warning to stop this warning.";

    public static bool ShouldShowAdminWarning()
    {
        return IsRunningAsAdmin() && !LaunchArgs.IsBypassArgumentPresent;
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
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return Environment.UserName == "root";
        }
        return false;
    }
}
