using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Jeek.Avalonia.Localization;
using Serilog;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Services.Sabr;

/// <summary>
/// Provisions and supervises the bgutil PO token provider using the bundled Deno runtime.
/// </summary>
internal static class BgUtilPotProvider
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(BgUtilPotProvider));

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } },
        Timeout = TimeSpan.FromMinutes(5)
    };

    private static readonly string RootPath = Path.Join(Program.UtilsPath, "bgutil");
    private static readonly string ServerPath = Path.Join(RootPath, "server");
    private static readonly string MainJsPath = Path.Join(ServerPath, "build", "main.js");

    /// <summary>
    /// yt-dlp searches each child of <c>--plugin-dirs</c> for a <c>yt_dlp_plugins</c> namespace.
    /// </summary>
    public static string PluginSearchDir =>
        IsAutoManaged ? Path.Join(ServerPath, "yt-dlp-plugins") : Program.UtilsPath;

    // Port conflicts change only the runtime URL, not the configured preference.
    private static string _baseUrl = ConfigManager.Config.SabrPotBaseUrl.TrimEnd('/');

    public static string BaseUrl => _baseUrl;

    /// <summary>
    /// Non-blocking readiness snapshot; use <see cref="WaitReadyAsync"/> to wait for startup.
    /// </summary>
    public static bool IsReady => _isReady;

    /// <summary>
    /// Shared yt-dlp provider arguments, already quoted for the command line.
    /// </summary>
    public static string[] ExtractorArgs =>
    [
        $"--plugin-dirs \"{PluginSearchDir}\"",
        $"--extractor-args \"youtubepot-bgutilhttp:base_url={BaseUrl}\"",
    ];

    public static int Port =>
        Uri.TryCreate(_baseUrl, UriKind.Absolute, out var uri) ? uri.Port : 4416;

    private static string PingUrl => $"{_baseUrl}/ping";

    // External providers are health-checked only; their lifecycle is managed by the operator.
    private static bool IsAutoManaged =>
        Uri.TryCreate(_baseUrl, UriKind.Absolute, out var uri) && uri.IsLoopback;

    private static readonly object InitLock = new();
    private static Task? _init;
    private static bool _backendReady;
    private static volatile bool _isReady;
    private static volatile bool _initFailed;
    private static Process? _server;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    static BgUtilPotProvider()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopServer();
    }

    /// <summary>
    /// Kills leftover processes matching the bundled Deno path; skips global Deno.
    /// Call once at startup, before starting the server.
    /// </summary>
    public static void KillOrphanedInstances()
    {
        if (LaunchArgs.UseGlobalPath)
            return;

        var denoPath = YtdlManager.DenoPath;
        if (!File.Exists(denoPath))
            return;

        string fullDenoPath;
        try { fullDenoPath = Path.GetFullPath(denoPath); }
        catch { return; }

        var processName = Path.GetFileNameWithoutExtension(denoPath);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                string? exePath;
                try { exePath = process.MainModule?.FileName; }
                catch { continue; } // Skip processes whose ownership cannot be verified.

                if (exePath is null || !string.Equals(Path.GetFullPath(exePath), fullDenoPath, PathComparison))
                    continue;

                Log.Information("Killing leftover Deno process {Pid} from a previous run", process.Id);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not kill Deno process {Pid}", process.Id);
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// Allows initialization after startup cleanup and dependency preparation have completed.
    /// Called by the backend, not by readiness checks. Safe to call repeatedly.
    /// </summary>
    public static void EnableStartup()
    {
        lock (InitLock)
        {
            _backendReady = true;
        }

        Ensure();
    }

    /// <summary>
    /// Starts initialization in the background once the backend is ready. Safe to call repeatedly.
    /// </summary>
    public static void Ensure()
    {
        if (!ConfigManager.Config.SabrRestreamEnabled)
            return;

        lock (InitLock)
        {
            // Dashboard verification can reach here before backend initialization even begins.
            if (!_backendReady)
                return;

            if (_init is { IsFaulted: true } or { IsCanceled: true })
            {
                _init = null;
                _initFailed = false;
            }
            _init ??= Task.Run(InitAsync);
        }
    }

    /// <summary>
    /// Waits for readiness, returning false on initialization failure, timeout, or cancellation.
    /// </summary>
    public static async Task<bool> WaitReadyAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (!ConfigManager.Config.SabrRestreamEnabled)
            return false;

        Ensure();

        if (_isReady)
            return true;

        using var activity = StatusService.Begin(StatusCategory.Provisioning,
            Localizer.Get("StatusProviderWaiting"), key: ToolVerifier.PotProviderKey);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_isReady)
                return true;
            if (_initFailed)
                return false;
            try { await Task.Delay(500, ct); }
            catch (OperationCanceledException) { return false; }
        }
        return _isReady;
    }

    private static async Task InitAsync()
    {
        if (IsAutoManaged)
        {
            ReassignPortIfInUse();
            using var activity = StatusService.Begin(StatusCategory.Provisioning,
                Localizer.Get("StatusProviderSetup"), key: ToolVerifier.PotProviderKey);
            try
            {
                EnsureInstalled();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to provision the bgutil PO token provider; SABR web playback will be unavailable until this is resolved");
                _initFailed = true;
                return;
            }
        }
        else
        {
            Log.Information("Using an externally-managed bgutil PO token provider at {Url}",
                ConfigManager.Config.SabrPotBaseUrl);
        }

        await SuperviseAsync();
    }

    private static void ReassignPortIfInUse()
    {
        var preferred = Port;
        if (!PortAudit.IsInUse(preferred))
            return;

        var who = PortAudit.DescribeListener(preferred);
        var free = PortAudit.FindFreePort(preferred);
        if (free == preferred)
        {
            Log.Warning("bgutil port {Port} is in use by {Process} and no free port was found nearby; " +
                        "the provider may fail to start", preferred, who);
            return;
        }

        _baseUrl = new UriBuilder(_baseUrl) { Port = free }.Uri.ToString().TrimEnd('/');
        Log.Warning("bgutil port {Preferred} is in use by {Process}; using port {Free} instead",
            preferred, who, free);
    }

    private static async Task SuperviseAsync()
    {
        while (true)
        {
            try
            {
                if (IsAutoManaged && (_server is null || _server.HasExited))
                {
                    _isReady = false;
                    StartServer();
                    // Give the BotGuard VM a moment to come up before the first health poll.
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }

                _isReady = await PingAsync();
            }
            catch (Exception ex)
            {
                _isReady = false;
                Log.Debug(ex, "bgutil supervisor iteration failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_isReady ? 15 : 3));
        }
        // ReSharper disable once FunctionNeverReturns
    }

    private static void EnsureInstalled()
    {
        if (!File.Exists(YtdlManager.DenoPath))
            throw new SabrException($"Deno runtime not found at {YtdlManager.DenoPath}; cannot run the PO token provider");

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new SabrException("The embedded bgutil server supports only Windows x64 and Linux x64");

        var packageName = OperatingSystem.IsWindows()
            ? "bgutil-pot-server-win-x64"
            : "bgutil-pot-server-linux-x64";
        var resourceName = $"VRCVideoCacher.{packageName}" + (OperatingSystem.IsWindows() ? ".zip" : ".tar.gz");
        var canvasPath = Path.Join("node_modules", "canvas", "build", "Release", "canvas.node");
        var installed = File.Exists(MainJsPath) && File.Exists(Path.Join(ServerPath, canvasPath));

        if (installed && Versions.CurrentVersion.BgUtil == Program.BgUtilsVersion)
        {
            Log.Debug("bgutil provider {Tag} already installed", Program.BgUtilsVersion);
            return;
        }

        SafeDelete(ServerPath);
        SafeDelete(Path.Join(Program.UtilsPath, "yt-dlp-plugins")); // legacy plugin location
        Log.Information("Installing bgutil PO token provider {Tag}...", Program.BgUtilsVersion);

        using var resource = typeof(BgUtilPotProvider).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new SabrException($"Embedded bgutil server resource not found: {resourceName}");

        var stagingPath = Path.Join(RootPath, $"install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingPath);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                ZipFile.ExtractToDirectory(resource, stagingPath);
            }
            else
            {
                using var gzip = new GZipStream(resource, CompressionMode.Decompress, leaveOpen: true);
                TarFile.ExtractToDirectory(gzip, stagingPath, overwriteFiles: false);
            }

            var extractedPath = Path.Join(stagingPath, packageName);
            if (!File.Exists(Path.Join(extractedPath, "build", "main.js")) ||
                !File.Exists(Path.Join(extractedPath, canvasPath)))
                throw new SabrException($"Embedded bgutil server package is incomplete: {resourceName}");

            if (Directory.Exists(ServerPath))
                SafeDelete(ServerPath);
            Directory.Move(extractedPath, ServerPath);
        }
        finally
        {
            SafeDelete(stagingPath);
        }

        Versions.CurrentVersion.BgUtil = Program.BgUtilsVersion;
        Versions.Save();
        Log.Information("bgutil PO token provider {Tag} installed.", Program.BgUtilsVersion);
    }

    private static void StartServer()
    {
        StopServer();

        // Keep the compiled entrypoint and bundled dependencies within the cwd-scoped permissions.
        var process = new Process
        {
            StartInfo =
            {
                FileName = YtdlManager.DenoPath,
                Arguments = $"run --no-config --no-lock --node-modules-dir=manual --cached-only --allow-env --allow-net --allow-ffi=. --allow-read=. build/main.js -p {Port}",
                WorkingDirectory = ServerPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
            EnableRaisingEvents = true,
        };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Log.Debug("[bgutil] {Line}", e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Log.Debug("[bgutil] {Line}", e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _server = process;
        Log.Information("Started bgutil PO token server on port {Port} (pid {Pid})", Port, process.Id);
    }

    private static void StopServer()
    {
        var process = Interlocked.Exchange(ref _server, null);
        if (process is null)
            return;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to stop bgutil server");
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>
    /// Checks health regardless of the SABR toggle, since legacy yt-dlp also uses the provider.
    /// </summary>
    public static Task<bool> IsRespondingAsync() => PingAsync();

    private static async Task<bool> PingAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await HttpClient.GetAsync(PingUrl, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(int exitCode, string output)> RunProcessAsync(
        string fileName, string arguments, string workingDirectory, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo =
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            }
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new SabrException($"'{Path.GetFileName(fileName)} {arguments}' timed out after {timeout.TotalMinutes:0} min");
        }

        var output = string.Join(Environment.NewLine, await stdout, await stderr);
        return (process.ExitCode, output);
    }

    private static void SafeDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not delete {Dir} before reinstall", dir);
        }
    }
}
