using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Startup TCP port checks: is a port already taken, who holds it, and (for ports we can move) the next
/// free one.
///
/// The managed API (<see cref="IPGlobalProperties.GetActiveTcpListeners"/>) can only tell us a port is in
/// use, not <i>by whom</i> — attributing it to a process needs a platform call (Win32 GetExtendedTcpTable
/// or, on Linux, <c>/proc</c> scanning). "Who" is best-effort and degrades to "an unknown process".
/// </summary>
public static class PortAudit
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(PortAudit));

    /// <summary>True if any process is already listening on <paramref name="port"/> (IPv4 or IPv6).</summary>
    public static bool IsInUse(int port)
    {
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(ep => ep.Port == port);
        }
        catch (Exception ex)
        {
            // Never block startup on a diagnostics failure — assume free and let the real bind decide.
            Log.Debug(ex, "Could not enumerate TCP listeners while checking port {Port}", port);
            return false;
        }
    }

    /// <summary>
    /// First free port at or after <paramref name="preferred"/> within <paramref name="span"/>. Returns
    /// <paramref name="preferred"/> itself as a "nothing free nearby" sentinel — callers that already know
    /// <paramref name="preferred"/> is taken should treat that as failure.
    /// </summary>
    public static int FindFreePort(int preferred, int span = 64)
    {
        for (var port = preferred; port < preferred + span && port <= 65535; port++)
        {
            if (!IsInUse(port))
                return port;
        }
        return preferred;
    }

    /// <summary>
    /// A human description of what's listening on <paramref name="port"/>, e.g. "deno (PID 1234)", or
    /// "an unknown process" when it can't be attributed.
    /// </summary>
    public static string DescribeListener(int port)
    {
        try
        {
            var pid = OperatingSystem.IsWindows() ? FindOwningPidWindows(port)
                : OperatingSystem.IsLinux() ? FindOwningPidLinux(port)
                : null;
            if (pid is not { } id)
                return "an unknown process";

            try
            {
                using var proc = Process.GetProcessById(id);
                return $"{proc.ProcessName} (PID {id})";
            }
            catch
            {
                return $"PID {id}";
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not identify the process listening on port {Port}", port);
            return "an unknown process";
        }
    }

    // ---------- Windows: iphlpapi GetExtendedTcpTable ----------

    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidListener = 3;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort,
        int ipVersion, int tableClass, int reserved);

    [SupportedOSPlatform("windows")]
    private static int? FindOwningPidWindows(int port) =>
        FindOwningPid(port, AfInet) ?? FindOwningPid(port, AfInet6);

    [SupportedOSPlatform("windows")]
    private static int? FindOwningPid(int port, int family)
    {
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TcpTableOwnerPidListener, 0);
        if (size <= 0)
            return null;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, family, TcpTableOwnerPidListener, 0) != 0)
                return null;

            var count = Marshal.ReadInt32(buffer); // dwNumEntries; rows follow immediately after.
            var rows = IntPtr.Add(buffer, 4);

            if (family == AfInet)
            {
                var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
                for (var i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(IntPtr.Add(rows, i * rowSize));
                    if (PortOf(row.LocalPort) == port)
                        return (int)row.OwningPid;
                }
            }
            else
            {
                var rowSize = Marshal.SizeOf<MibTcp6RowOwnerPid>();
                for (var i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(IntPtr.Add(rows, i * rowSize));
                    if (PortOf(row.LocalPort) == port)
                        return (int)row.OwningPid;
                }
            }

            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>The port field is a DWORD holding the port in network byte order in its low 16 bits.</summary>
    private static int PortOf(uint dwPort) => (int)(((dwPort & 0xFF) << 8) | ((dwPort >> 8) & 0xFF));

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    // ---------- Linux: /proc (best-effort) ----------

    [SupportedOSPlatform("linux")]
    private static int? FindOwningPidLinux(int port)
    {
        var inode = FindListeningInode("/proc/net/tcp", port) ?? FindListeningInode("/proc/net/tcp6", port);
        if (inode is null)
            return null;

        var needle = $"socket:[{inode}]";
        foreach (var pidDir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(pidDir), out var pid))
                continue;
            try
            {
                foreach (var fd in Directory.EnumerateFileSystemEntries($"{pidDir}/fd"))
                {
                    var target = File.ResolveLinkTarget(fd, false)?.Name;
                    if (target == needle)
                        return pid;
                }
            }
            catch
            {
                // Not our process to read (permissions) or it vanished — keep scanning.
            }
        }
        return null;
    }

    /// <summary>The inode of the LISTEN socket on <paramref name="port"/> in a /proc/net/tcp[6] table.</summary>
    private static string? FindListeningInode(string procFile, int port)
    {
        if (!File.Exists(procFile))
            return null;

        var hexPort = port.ToString("X4");
        foreach (var line in File.ReadLines(procFile).Skip(1))
        {
            var cols = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // cols: sl local_address rem_address st ... inode
            if (cols.Length < 10)
                continue;
            if (cols[3] != "0A") // TCP_LISTEN
                continue;
            var colon = cols[1].LastIndexOf(':');
            if (colon < 0 || !cols[1][(colon + 1)..].Equals(hexPort, StringComparison.OrdinalIgnoreCase))
                continue;
            return cols[9];
        }
        return null;
    }

    /// <summary>
    /// The web server (port 9696) is fixed — the browser extension and yt-dlp-stub hardcode it, so it
    /// cannot be moved. If it's already taken, alert the user (Error ⇒ popup in GUI, console otherwise)
    /// with the offending process, before EmbedIO throws an opaque bind error.
    /// </summary>
    public static void CheckWebServerPort()
    {
        var port = Uri.TryCreate(ConfigManager.Config.YtdlpWebServerUrl, UriKind.Absolute, out var uri)
            ? uri.Port
            : 9696;

        if (!IsInUse(port))
            return;

        var who = DescribeListener(port);
        Log.Error(
            "Port {Port} is already in use by {Process}. VRCVideoCacher needs this exact port for the " +
            "browser extension and game integration and cannot use another one — close that application " +
            "and restart VRCVideoCacher.", port, who);
    }
}
