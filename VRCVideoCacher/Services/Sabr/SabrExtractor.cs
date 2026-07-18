using System.Diagnostics;
using System.Text;
using Newtonsoft.Json.Linq;
using Serilog;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Services.Sabr;

/// <summary>
/// Turns a YouTube URL into a <see cref="SabrSource"/> by running yt-dlp once, purely as a link
/// extractor. yt-dlp's <c>-J</c> output already carries every SABR handle we need under each format's
/// <c>_sabr_config</c>, so we never reimplement Innertube — we only take over the protocol itself.
/// </summary>
internal static class SabrExtractor
{
    // ClientName enum values from the innertube schema. We do not care WHICH client yt-dlp hands us — it
    // varies over time and by account — only that we can map its name to the enum the request needs.
    private static readonly Dictionary<string, int> ClientNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WEB"] = 1, ["MWEB"] = 2, ["ANDROID"] = 3, ["IOS"] = 5, ["TVHTML5"] = 7, ["TVLITE"] = 8,
        ["ANDROID_VR"] = 28, ["ANDROID_MUSIC"] = 21, ["ANDROID_TV"] = 23, ["IOS_MUSIC"] = 26,
        ["WEB_EMBEDDED_PLAYER"] = 56, ["WEB_MUSIC"] = 61, ["WEB_CREATOR"] = 62, ["TVHTML5_SIMPLY"] = 75,
        ["WEB_REMIX"] = 67, ["VISIONOS"] = 101,
    };

    /// <summary>
    /// Clients that expect <c>MediaCapabilities</c> on the request. Web clients must NOT be sent it — the
    /// server then ignores preferred_*_format_ids and serves whatever it likes. This is the only reason
    /// we care which client we ended up on.
    /// </summary>
    private static readonly HashSet<string> MobileClients = new(StringComparer.OrdinalIgnoreCase)
        { "ANDROID_VR", "ANDROID", "ANDROID_TV", "ANDROID_MUSIC", "IOS", "IOS_MUSIC", "VISIONOS" };

    /// <summary>
    /// How long a playback request waits for the PO token provider to be ready before failing cleanly.
    /// The provider is warmed at startup, so by the time a video plays it is normally already up; this is
    /// only the ceiling for the case where a request arrives while it is still coming online.
    /// </summary>
    private static readonly TimeSpan PotProviderTimeout = TimeSpan.FromSeconds(45);

    /// <param name="fmp4Only">
    /// Restrict to fMP4 tracks (H.264 + AAC). Required when the fragments are served to HLS directly:
    /// HLS cannot carry WebM segments, and YouTube only ships Opus and VP9/AV1 in WebM.
    /// </param>
    public static async Task<SabrSource> ExtractAsync(string videoUrl, int maxHeight, string ytdlpPath,
        string? cookiesPath, ILogger log, CancellationToken ct = default, bool fmp4Only = false)
    {
        // The web client's SABR formats require a GVS PO token; make sure the provider that mints it is up
        // before we extract, so a missing token surfaces as a clean failure here rather than a mid-stream
        // attestation stall.
        if (!await BgUtilPotProvider.WaitReadyAsync(PotProviderTimeout, ct))
            throw new SabrException(
                "PO token provider (bgutil) is not ready, so the web SABR client cannot be used.");

        var json = await RunYtdlpAsync(videoUrl, ytdlpPath, cookiesPath, log, ct);

        var videoId = json["id"]?.Value<string>()
                      ?? throw new SabrException("yt-dlp returned no video id");

        // We drive SABR through the WEB client only. Its formats carry a GVS PO token (minted by the
        // bgutil provider and surfaced in _sabr_config.po_token); that token, plus the web client_info, is
        // what the SABR server attests against.
        var formats = (json["formats"] as JArray ?? [])
            .Where(f => f["protocol"]?.Value<string>() == "sabr")
            .Where(f => IsClient(f, "web"))
            .ToList();

        if (formats.Count == 0)
            throw new SabrException(
                "No web-client SABR formats found. yt-dlp may not have used the web client — check cookies " +
                "and that the bgutil plugin loaded (--plugin-dirs).");

        var client = ClientOf(formats[0]);

        var audio = PickAudio(formats, fmp4Only)
                    ?? throw new SabrException(
                        fmp4Only ? "No AAC (fMP4) SABR audio format found" : "No SABR audio format found");
        var video = PickVideo(formats, maxHeight, fmp4Only)
                    ?? throw new SabrException(fmp4Only
                        ? $"No H.264 (fMP4) SABR video format found at or below {maxHeight}p"
                        : $"No SABR video format found at or below {maxHeight}p");

        var hdr = IsHdr(video);
        var config = video["_sabr_config"]!;

        // live_status rides in _sabr_config (yt-dlp _video.py:3765). Only "is_live" is a live fetch —
        // "post_live" is a finished broadcast being turned into a VOD, which behaves as a VOD and does
        // have an index. target_duration_sec (:3814) is the segment length; measured as 2 on a real
        // stream, so never assume a default.
        var liveStatus = config["live_status"]?.Value<string>();
        var isLive = string.Equals(liveStatus, "is_live", StringComparison.Ordinal);
        var targetDurationSec = config["target_duration_sec"]?.Value<int?>() ?? 0;

        log.Information("SABR formats for {VideoId}{Live}: video {VideoFormat} ({VCodec} {Height}p {Range}) + audio {AudioFormat} ({ACodec})",
            videoId,
            isLive ? $" [LIVE, {targetDurationSec}s segments]" : string.Empty,
            video["format_id"]?.Value<string>(), video["vcodec"]?.Value<string>(), video["height"]?.Value<int>(),
            hdr ? "HDR" : "SDR",
            audio["format_id"]?.Value<string>(), audio["acodec"]?.Value<string>());

        var poToken = config["po_token"]?.Value<string>();
        if (string.IsNullOrEmpty(poToken))
            throw new SabrException(
                "The web SABR format carried no po_token — the bgutil PO token provider did not supply one. " +
                "SABR playback cannot proceed without it.");

        return new SabrSource
        {
            VideoId = videoId,
            // Every SABR format shares the same ABR streaming URL and ustreamer config.
            AbrStreamingUrl = video["url"]?.Value<string>()
                              ?? throw new SabrException("SABR format carried no streaming URL"),
            UstreamerConfig = config["video_playback_ustreamer_config"]?.Value<string>()
                              ?? throw new SabrException("SABR format carried no ustreamer config"),
            ClientInfo = ParseClientInfo(config["client_info"]),
            SendMediaCapabilities = MobileClients.Contains(client),
            AudioFormat = ParseFormatId(audio["_sabr_config"]!),
            VideoFormat = ParseFormatId(config),
            Hdr = hdr,
            PoToken = poToken,
            VideoCodec = video["vcodec"]?.Value<string>() ?? "avc1.4d401f",
            AudioCodec = audio["acodec"]?.Value<string>() ?? "mp4a.40.2",
            Width = video["width"]?.Value<int>() ?? 1920,
            Height = video["height"]?.Value<int>() ?? 1080,
            Bandwidth = (long)((Bitrate(video) + Bitrate(audio)) * 1000),
            IsLive = isLive,
            TargetDurationSec = targetDurationSec,
        };
    }

    /// <summary>
    /// Opus by preference — the better codec — except in fMP4-only mode, where it isn't an option:
    /// YouTube ships Opus exclusively in WebM, which HLS cannot carry as segments.
    /// </summary>
    private static JToken? PickAudio(List<JToken> formats, bool fmp4Only)
    {
        var audio = formats.Where(f => f["acodec"]?.Value<string>() is { } a && a != "none"
                                       && f["vcodec"]?.Value<string>() is "none" or null).ToList();

        var aac = audio.Where(f => IsAac(f)).MaxBy(Bitrate);
        if (fmp4Only)
            return aac;

        return audio.Where(f => f["acodec"]!.Value<string>()!.StartsWith("opus", StringComparison.Ordinal))
                   .MaxBy(Bitrate)
               ?? aac
               ?? audio.MaxBy(Bitrate);
    }

    private static string ClientOf(JToken format) =>
        format["_sabr_config"]?["client_name"]?.Value<string>() ?? "";

    private static bool IsClient(JToken format, string client) =>
        ClientOf(format).Equals(client, StringComparison.OrdinalIgnoreCase);

    private static bool IsAac(JToken format) =>
        format["acodec"]?.Value<string>()?.StartsWith("mp4a", StringComparison.Ordinal) == true;

    private static bool IsH264(JToken format) =>
        format["vcodec"]?.Value<string>()?.StartsWith("avc1", StringComparison.Ordinal) == true;

    /// <summary>
    /// Highest resolution within budget; SDR before HDR, then H.264, VP9, AV1 at equal height.
    ///
    /// SDR ranks above HDR deliberately: HDR tone-mapping is not something VRChat's players handle
    /// well, and HDR is also the higher-bitrate variant, so ordering purely on bitrate would silently
    /// pick it (which is exactly the bug that made 4K videos fail).
    /// </summary>
    private static JToken? PickVideo(List<JToken> formats, int maxHeight, bool fmp4Only) =>
        formats
            .Where(f => f["vcodec"]?.Value<string>() is { } v && v != "none")
            .Where(f => !fmp4Only || IsH264(f)) // VP9/AV1 are WebM-only; HLS can't carry them natively
            .Where(f => (f["height"]?.Value<int>() ?? 0) <= maxHeight)
            .OrderByDescending(f => f["height"]?.Value<int>() ?? 0)
            .ThenBy(f => IsHdr(f) ? 1 : 0)
            .ThenBy(CodecRank)
            .ThenByDescending(Bitrate)
            .FirstOrDefault();

    private static bool IsHdr(JToken format) =>
        format["dynamic_range"]?.Value<string>() is { } range
        && !range.Equals("SDR", StringComparison.OrdinalIgnoreCase);

    private static int CodecRank(JToken format) => format["vcodec"]?.Value<string>() switch
    {
        { } v when v.StartsWith("avc1", StringComparison.Ordinal) => 0,
        { } v when v.StartsWith("vp9", StringComparison.Ordinal) || v.StartsWith("vp09", StringComparison.Ordinal) => 1,
        _ => 2,
    };

    private static long Bitrate(JToken format) => format["tbr"]?.Value<long?>() ?? format["filesize"]?.Value<long?>() ?? 0;

    private static FormatId ParseFormatId(JToken sabrConfig) => new()
    {
        Itag = sabrConfig["itag"]?.Value<int>() ?? throw new SabrException("SABR format carried no itag"),
        Lmt = sabrConfig["last_modified"]?.Value<ulong?>(),
        Xtags = sabrConfig["xtags"]?.Value<string>(),
    };

    private static ClientInfo ParseClientInfo(JToken? json)
    {
        if (json is null)
            throw new SabrException("SABR config carried no client_info");

        var name = json["client_name"]?.Value<string>();
        if (name is null || !ClientNames.TryGetValue(name, out var clientName))
            throw new SabrException($"Unknown SABR client '{name}'");

        return new ClientInfo
        {
            ClientName = clientName,
            ClientVersion = json["client_version"]?.Value<string>(),
            OsName = json["os_name"]?.Value<string>(),
            OsVersion = json["os_version"]?.Value<string>(),
            DeviceMake = json["device_make"]?.Value<string>(),
            DeviceModel = json["device_model"]?.Value<string>(),
            AndroidSdkVersion = json["android_sdk_version"]?.Value<int?>(),
        };
    }

    private static async Task<JObject> RunYtdlpAsync(string videoUrl, string ytdlpPath, string? cookiesPath,
        ILogger log, CancellationToken ct)
    {
        var args = new StringBuilder();
        // formats=duplicate exposes the SABR variants alongside the normal ones. player_client=web pins us
        // to the web client, whose SABR formats require a GVS PO token that the bgutil plugin supplies.
        args.Append("-J --no-warnings --extractor-args \"youtube:formats=duplicate;player_client=web\" ");
        // The web client's streaming URL carries an 'n' challenge yt-dlp must descramble during extraction,
        // which needs a JS runtime. (android_vr didn't — REQUIRE_JS_PLAYER was false there.)
        if (File.Exists(YtdlManager.DenoPath))
            args.Append($"--js-runtimes deno:\"{YtdlManager.DenoPath}\" ");
        // The bgutil PO token plugin: point yt-dlp at the plugin search dir (--plugin-dirs; yt-dlp finds
        // <dir>/yt-dlp-plugins/yt_dlp_plugins under it) and tell the plugin where the server is. We pass
        // base_url explicitly rather than relying on the plugin's auto-detect default (127.0.0.1) — the
        // server binds IPv6 "::", so a bare 127.0.0.1 is refused on Windows; "localhost" (the
        // SabrPotBaseUrl default) resolves to both families and connects. See ConfigManager.
        args.Append($"--plugin-dirs \"{BgUtilPotProvider.PluginSearchDir}\" ");
        args.Append($"--extractor-args \"youtubepot-bgutilhttp:base_url={BgUtilPotProvider.BaseUrl}\" ");
        if (!string.IsNullOrEmpty(cookiesPath) && File.Exists(cookiesPath))
            args.Append($"--cookies \"{cookiesPath}\" ");
        args.Append($"\"{videoUrl}\"");

        using var process = new Process
        {
            StartInfo =
            {
                FileName = ytdlpPath,
                Arguments = args.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
        };

        // yt-dlp rewrites the cookie jar on exit; two of them at once corrupt the session and get us
        // bot-checked. This shares the gate with the app's other yt-dlp calls. See YtdlCookieJar.
        using var cookieJar = await YtdlCookieJar.AcquireAsync(ct);

        log.Debug("[sabr-extract] {File} {Args}", Path.GetFileName(ytdlpPath), args);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new SabrException($"yt-dlp extraction failed: {(await stderr).Trim()}");

        return JObject.Parse(await stdout);
    }
}
