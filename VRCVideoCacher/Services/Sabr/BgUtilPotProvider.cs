using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;
using Serilog;
using SharpCompress.Readers;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Services.Sabr;

/// <summary>
/// Auto-provisions and supervises <c>bgutil-ytdlp-pot-provider</c> — the PO token provider yt-dlp's own
/// guide recommends — so the SABR extractor can use the <b>web</b> client, which (unlike android_vr)
/// requires a GVS PO token.
///
/// This runs the provider's token generator on the Deno runtime the app already ships and manages, so
/// there is no Node or Docker dependency. Two on-disk pieces come out of one download of the provider's
/// source tarball at a pinned tag:
/// <list type="bullet">
///   <item><b>server/</b> — the token generator (a Deno/Node project). We <c>deno install</c> its npm
///     deps (including the native <c>canvas</c> package) and run its HTTP server on 127.0.0.1:4416.</item>
///   <item><b>yt_dlp_plugins/</b> — the Python plugin the frozen yt-dlp loads (via <c>--plugin-dirs</c>)
///     to talk to that server. With the server on the default port, the plugin auto-detects it, so no
///     extractor-arg is needed.</item>
/// </list>
///
/// Failure is deliberately non-fatal: if provisioning fails (most likely the native <c>canvas</c> build
/// on Linux), the provider simply never becomes ready and SABR reports a clean "provider not ready"
/// instead of crashing or stalling mid-stream.
/// </summary>
internal static class BgUtilPotProvider
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(BgUtilPotProvider));

    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/Brainicism/bgutil-ytdlp-pot-provider/releases/latest";

    /// <summary>
    /// Last resort only: the tag to install if the release check can't reach GitHub AND nothing is
    /// installed yet. Normal operation tracks the latest release (the plugin is coupled to yt-dlp's PO
    /// token API, and our yt-dlp auto-updates, so a stale plugin is the bigger risk).
    /// </summary>
    private const string FallbackTag = "1.3.1";

    private static string SourceTarballUrl(string tag) =>
        $"https://github.com/Brainicism/bgutil-ytdlp-pot-provider/archive/refs/tags/{tag}.tar.gz";

    private static readonly HttpClient HttpClient = new()
    {
        // GitHub's archive endpoint is happy without auth; a UA is polite and avoids the odd 403.
        DefaultRequestHeaders = { { "User-Agent", "VRCVideoCacher" } },
        Timeout = TimeSpan.FromMinutes(5),
    };

    private static readonly string RootPath = Path.Join(Program.UtilsPath, "bgutil");
    private static readonly string ServerPath = Path.Join(RootPath, "server");
    private static readonly string NodeModulesPath = Path.Join(ServerPath, "node_modules");
    private static readonly string MainTsPath = Path.Join(ServerPath, "src", "main.ts");

    /// <summary>
    /// Where the plugin's <c>yt_dlp_plugins</c> namespace package is written:
    /// <c>&lt;UtilsPath&gt;/yt-dlp-plugins/yt_dlp_plugins/…</c>. The <c>yt-dlp-plugins</c> folder name is
    /// load-bearing — see <see cref="PluginSearchDir"/>.
    /// </summary>
    private static readonly string PluginDir = Path.Join(Program.UtilsPath, "yt-dlp-plugins");

    /// <summary>
    /// The directory handed to yt-dlp's <c>--plugin-dirs</c>. yt-dlp searches a plugin dir for a child
    /// <c>yt-dlp-plugins/yt_dlp_plugins</c> package, so we pass the <b>parent</b> of our
    /// <c>yt-dlp-plugins</c> folder — pointing <c>--plugin-dirs</c> straight at that folder finds nothing
    /// (verified against the shipped yt-dlp: parent loads <c>bgutil:http</c>, the folder itself loads none).
    /// </summary>
    public static string PluginSearchDir { get; } = Program.UtilsPath;

    // The URL we ACTUALLY use. Defaults to the configured (preferred) value; if that port is already
    // taken at startup we rebase this onto a free one (see ReassignPortIfInUse). Config stays the
    // preferred value — a transient conflict must not rewrite Config.json. Everything derives from this.
    private static string _baseUrl = ConfigManager.Config.SabrPotBaseUrl.TrimEnd('/');

    /// <summary>The provider URL actually in use (may differ from config if the port had to be reassigned).</summary>
    public static string BaseUrl => _baseUrl;

    private static int Port =>
        Uri.TryCreate(_baseUrl, UriKind.Absolute, out var uri) ? uri.Port : 4416;

    private static string PingUrl => $"{_baseUrl}/ping";

    /// <summary>
    /// True when the provider is local (loopback) and we own its lifecycle. A non-loopback URL means the
    /// operator runs the provider themselves (Docker, another host); we then only health-check it and
    /// never download/install/spawn/reassign anything.
    /// </summary>
    private static bool IsAutoManaged =>
        Uri.TryCreate(_baseUrl, UriKind.Absolute, out var uri) && uri.IsLoopback;

    // Provisioning + supervision run at most once; readiness is polled by WaitReadyAsync.
    private static readonly object InitLock = new();
    private static Task? _init;
    private static volatile bool _isReady;
    private static volatile bool _initFailed;
    private static Process? _server;

    static BgUtilPotProvider()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopServer();
    }

    /// <summary>
    /// Kick off provisioning + server startup in the background if it hasn't started already. Safe to
    /// call repeatedly; returns immediately. Warm this at app startup so the provider is usually ready by
    /// the first SABR playback.
    /// </summary>
    public static void Ensure()
    {
        if (!ConfigManager.Config.SabrRestreamEnabled)
            return;

        lock (InitLock)
        {
            if (_init is { IsFaulted: true } or { IsCanceled: true })
            {
                // A prior attempt died outright (not just "unhealthy"); allow a fresh try.
                _init = null;
                _initFailed = false;
            }
            _init ??= Task.Run(InitAsync);
        }
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the provider to answer <c>/ping</c>. Returns false
    /// (never throws) if provisioning failed or the deadline passes — the caller turns that into a clean
    /// "provider not ready" rather than a mid-stream stall.
    /// </summary>
    public static async Task<bool> WaitReadyAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (!ConfigManager.Config.SabrRestreamEnabled)
            return false;

        Ensure();

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
            try
            {
                await EnsureInstalledAsync();
            }
            catch (Exception ex)
            {
                // Almost always the native canvas build (see class remarks). Loud, but not fatal.
                Log.Error(ex, "Failed to provision the bgutil PO token provider; SABR web playback will be " +
                              "unavailable until this is resolved (often a native 'canvas' build failure on Linux)");
                _initFailed = true;
                return;
            }
        }
        else
        {
            Log.Information("Using an externally-managed bgutil PO token provider at {Url}",
                ConfigManager.Config.SabrPotBaseUrl);
        }

        // Keep the (local) server alive for the life of the app; for an external provider this loop only
        // health-checks. The first iteration starts the server and the health poll flips _isReady.
        await SuperviseAsync();
    }

    /// <summary>
    /// If our preferred port is already taken (another app — or an orphaned bgutil server from a
    /// hard-killed previous run), move to a free one instead of failing. We can do this because we own the
    /// server: it launches with <c>-p {Port}</c> and the extractor is told the same <see cref="BaseUrl"/>.
    /// </summary>
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

    /// <summary>
    /// Downloads the provider source at the pinned tag, lays out the server and plugin, and runs
    /// <c>deno install</c>. Idempotent: skipped once the marker matches and the artefacts are on disk.
    /// </summary>
    private static async Task EnsureInstalledAsync()
    {
        if (!File.Exists(YtdlManager.DenoPath))
            throw new SabrException($"Deno runtime not found at {YtdlManager.DenoPath}; cannot run the PO token provider");

        var installed = Directory.Exists(NodeModulesPath)
                        && File.Exists(MainTsPath)
                        && Directory.Exists(Path.Join(PluginDir, "yt_dlp_plugins"));

        // Track the latest release, like the rest of the stack. If the check can't reach GitHub we keep
        // whatever is installed; only a completely fresh machine falls back to a known tag.
        var targetTag = await ResolveLatestTagAsync() ?? (installed ? null : FallbackTag);
        if (targetTag is null)
        {
            Log.Information("Could not check for bgutil updates; using the installed provider {Tag}",
                string.IsNullOrEmpty(Versions.CurrentVersion.BgUtil) ? "(unknown)" : Versions.CurrentVersion.BgUtil);
            return;
        }

        if (installed && Versions.CurrentVersion.BgUtil == targetTag)
        {
            Log.Debug("bgutil provider {Tag} already installed", targetTag);
            return;
        }

        Log.Information("Installing bgutil PO token provider {Tag}...", targetTag);

        // Start from clean directories so a version change never leaves stale files behind.
        SafeDelete(ServerPath);
        SafeDelete(PluginDir);
        Directory.CreateDirectory(ServerPath);
        Directory.CreateDirectory(PluginDir);

        await DownloadAndExtractAsync(targetTag);

        Log.Information("Running 'deno install' for the bgutil server (this fetches npm deps incl. native canvas)...");
        await RunDenoInstallAsync();

        Versions.CurrentVersion.BgUtil = targetTag;
        Versions.Save();
        Log.Information("bgutil PO token provider {Tag} installed.", targetTag);
    }

    /// <summary>The latest release tag, or null if GitHub could not be reached (offline / rate-limited).</summary>
    private static async Task<string?> ResolveLatestTagAsync()
    {
        try
        {
            using var response = await HttpClient.GetAsync(LatestReleaseApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("bgutil release check failed: {Status}", response.StatusCode);
                return null;
            }
            var release = JsonConvert.DeserializeObject<GitHubRelease>(await response.Content.ReadAsStringAsync());
            return string.IsNullOrWhiteSpace(release?.tag_name) ? null : release.tag_name;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "bgutil release check failed");
            return null;
        }
    }

    /// <summary>
    /// Pulls the two subtrees we need out of the source tarball in a single pass: <c>server/</c> → our
    /// server dir, and <c>plugin/yt_dlp_plugins/</c> → the plugin dir (directly, so --plugin-dirs finds
    /// it). GitHub prefixes every entry with a <c>&lt;repo&gt;-&lt;tag&gt;/</c> top folder, which we strip.
    /// </summary>
    private static async Task DownloadAndExtractAsync(string tag)
    {
        await using var stream = await HttpClient.GetStreamAsync(SourceTarballUrl(tag));
        var reader = await ReaderFactory.OpenAsyncReader(stream);
        try
        {
            while (await reader.MoveToNextEntryAsync())
            {
                if (reader.Entry.IsDirectory || reader.Entry.Key is null)
                    continue;

                var key = reader.Entry.Key.Replace('\\', '/');
                var slash = key.IndexOf('/');
                if (slash < 0)
                    continue;
                var rel = key[(slash + 1)..]; // drop the "<repo>-<tag>/" prefix

                string? dest = rel switch
                {
                    _ when rel.StartsWith("server/", StringComparison.Ordinal)
                        => Path.Join(ServerPath, rel["server/".Length..]),
                    // Keep the "yt_dlp_plugins/..." tail so PluginDir directly contains the package.
                    _ when rel.StartsWith("plugin/yt_dlp_plugins/", StringComparison.Ordinal)
                        => Path.Join(PluginDir, rel["plugin/".Length..]),
                    _ => null,
                };
                if (dest is null)
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                await using var outStream = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
                await using var entryStream = await reader.OpenEntryStreamAsync();
                await entryStream.CopyToAsync(outStream);
            }
        }
        finally
        {
            await reader.DisposeAsync();
        }

        if (!File.Exists(MainTsPath))
            throw new SabrException("bgutil source tarball did not contain server/src/main.ts");
    }

    private static async Task RunDenoInstallAsync()
    {
        // Mirrors the provider's documented Deno flow: --allow-scripts lets canvas's postinstall run,
        // --frozen pins to the committed deno.lock.
        var (exit, output) = await RunProcessAsync(
            YtdlManager.DenoPath, "install --allow-scripts=npm:canvas --frozen", ServerPath, TimeSpan.FromMinutes(5));

        if (exit != 0)
            throw new SabrException($"'deno install' failed (exit {exit}): {output.Trim()}");
    }

    private static void StartServer()
    {
        StopServer();

        // Run from node_modules so canvas's native lib is reachable under the cwd-scoped --allow-ffi=. /
        // --allow-read=. grants; the entrypoint is one level up.
        var process = new Process
        {
            StartInfo =
            {
                FileName = YtdlManager.DenoPath,
                Arguments = $"run --allow-env --allow-net --allow-ffi=. --allow-read=. ../src/main.ts -p {Port}",
                WorkingDirectory = NodeModulesPath,
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
            },
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
