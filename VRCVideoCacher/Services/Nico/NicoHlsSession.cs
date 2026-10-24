using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Serilog;

namespace VRCVideoCacher.Services.Nico;

internal sealed partial class NicoHlsSession : INicoSession
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private readonly string? _audioInitUrl;
    private readonly string? _audioKeyIv;
    private readonly string? _audioKeyUrl;
    private readonly List<NicoSegmentItem> _audioSegments;

    private readonly ConcurrentDictionary<int, Lazy<Task>> _building = new();
    private readonly Dictionary<string, string> _cookies;

    private readonly string _dir;

    private readonly List<long> _durationsMs = [];
    private readonly HttpClient _httpClient;
    private readonly ILogger _log;
    private readonly NicoSegmentMuxer _muxer;
    private readonly List<long> _startMs = [];

    private readonly string? _videoInitUrl;
    private readonly string? _videoKeyIv;
    private readonly string? _videoKeyUrl;
    private readonly List<NicoSegmentItem> _videoSegments;
    private byte[]? _cachedAudioInit;
    private byte[]? _cachedAudioKey;

    private byte[]? _cachedVideoInit;
    private byte[]? _cachedVideoKey;

    private NicoHlsSession(string videoId, string dir, Dictionary<string, string> cookies, NicoSegmentMuxer muxer,
        HttpClient httpClient,
        string? videoInitUrl, string? videoKeyUrl, string? videoKeyIv, List<NicoSegmentItem> videoSegments,
        string? audioInitUrl, string? audioKeyUrl, string? audioKeyIv, List<NicoSegmentItem> audioSegments, ILogger log)
    {
        PlaybackUrl = videoId;
        _dir = dir;
        _cookies = cookies;
        _muxer = muxer;
        _httpClient = httpClient;
        _videoInitUrl = videoInitUrl;
        _videoKeyUrl = videoKeyUrl;
        _videoKeyIv = videoKeyIv;
        _videoSegments = videoSegments;
        _audioInitUrl = audioInitUrl;
        _audioKeyUrl = audioKeyUrl;
        _audioKeyIv = audioKeyIv;
        _audioSegments = audioSegments;
        _log = log;

        long currentMs = 0;
        foreach (var seg in _videoSegments)
        {
            var ms = (long)Math.Round(seg.Duration * 1000.0);
            if (ms <= 0) ms = 6000;
            _startMs.Add(currentMs);
            _durationsMs.Add(ms);
            currentMs += ms;
        }
    }

    public double TotalDurationSeconds => _durationsMs.Sum() / 1000.0;

    public string PlaybackUrl => $"{ConfigManager.Config.YtdlpWebServerUrl.TrimEnd('/')}/nico/{field}/index.m3u8";
    public DateTime LastAccess { get; private set; } = DateTime.UtcNow;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }
        catch
        {
            // ignore
        }
    }

    public void Touch() => LastAccess = DateTime.UtcNow;

    public async Task EnsureAsync(string fileName)
    {
        Touch();
        if (fileName.Equals("index.m3u8", StringComparison.OrdinalIgnoreCase))
            return;

        if (fileName.Equals("init.mp4", StringComparison.OrdinalIgnoreCase))
        {
            await BuildSegmentAsync(0);
            return;
        }

        if (TryParseSegmentIndex(fileName, out var index))
        {
            await BuildSegmentAsync(index);
            StartPrebuild(index);
        }
    }

    [GeneratedRegex(@"#EXT-X-MEDIA:TYPE=AUDIO[^\n]*URI=""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex AudioMediaRegex();

    [GeneratedRegex(@"#EXT-X-STREAM-INF:([^\n]*BANDWIDTH=(\d+)[^\n]*)", RegexOptions.Compiled)]
    private static partial Regex StreamInfRegex();

    [GeneratedRegex(@"#EXT-X-MAP:URI=""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex MapUriRegex();

    [GeneratedRegex(@"#EXT-X-KEY:METHOD=AES-128,URI=""([^""]+)""(?:,IV=([0-9a-fA-FxX]+))?", RegexOptions.Compiled)]
    private static partial Regex KeyRegex();

    [GeneratedRegex(@"#EXTINF:([0-9.]+),", RegexOptions.Compiled)]
    private static partial Regex ExtInfRegex();

    public static async Task<NicoHlsSession> StartAsync(string videoId, string masterUrl,
        Dictionary<string, string> cookies, string rootDir, HttpClient httpClient, NicoSegmentMuxer muxer, ILogger log)
    {
        var dir = Path.Combine(rootDir, videoId);
        if (Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // ignore
            }
        }
        Directory.CreateDirectory(dir);

        using var cts = new CancellationTokenSource(StartTimeout);

        var masterText = await FetchTextAsync(httpClient, masterUrl, cookies, cts.Token);
        var (videoVariantUrl, audioVariantUrl) = ParseMasterPlaylist(masterText, masterUrl);

        if (string.IsNullOrEmpty(videoVariantUrl))
            throw new InvalidOperationException($"No video variant found in master playlist for {videoId}");

        var videoText = await FetchTextAsync(httpClient, videoVariantUrl, cookies, cts.Token);
        var videoSegments = new List<NicoSegmentItem>();
        ParseVariantPlaylist(videoText, videoVariantUrl, out var videoInitUrl, out var videoKeyUrl, out var videoKeyIv,
            videoSegments);

        if (videoSegments.Count == 0)
            throw new InvalidOperationException($"No video segments found in video playlist for {videoId}");

        var audioSegments = new List<NicoSegmentItem>();
        string? audioInitUrl = null;
        string? audioKeyUrl = null;
        string? audioKeyIv = null;
        if (!string.IsNullOrEmpty(audioVariantUrl))
        {
            var audioText = await FetchTextAsync(httpClient, audioVariantUrl, cookies, cts.Token);
            ParseVariantPlaylist(audioText, audioVariantUrl, out audioInitUrl, out audioKeyUrl, out audioKeyIv,
                audioSegments);
        }

        var session = new NicoHlsSession(videoId, dir, cookies, muxer, httpClient, videoInitUrl, videoKeyUrl, videoKeyIv,
            videoSegments, audioInitUrl, audioKeyUrl, audioKeyIv, audioSegments, log);

        var playlistContent = session.BuildPlaylist();
        await File.WriteAllTextAsync(Path.Combine(dir, "index.m3u8"), playlistContent, cts.Token);
        log.Information("NicoNico HLS ready for {VideoId}: {Count} segments, {Duration:0.0}s", videoId,
            videoSegments.Count, session.TotalDurationSeconds);

        _ = Task.Run(() => session.BuildSegmentAsync(0), cts.Token);

        return session;
    }

    private string BuildPlaylist()
    {
        var targetDuration = (int)Math.Max(1, Math.Ceiling(_durationsMs.Max() / 1000.0));
        var sb = new StringBuilder();
        sb.Append("#EXTM3U\n");
        sb.Append("#EXT-X-VERSION:7\n");
        sb.Append($"#EXT-X-TARGETDURATION:{targetDuration}\n");
        sb.Append("#EXT-X-MEDIA-SEQUENCE:0\n");
        sb.Append("#EXT-X-PLAYLIST-TYPE:VOD\n");
        sb.Append("#EXT-X-INDEPENDENT-SEGMENTS\n");
        sb.Append("#EXT-X-MAP:URI=\"init.mp4\"\n");

        for (var i = 0; i < _videoSegments.Count; i++)
        {
            var seconds = (_durationsMs[i] / 1000.0).ToString("F6", CultureInfo.InvariantCulture);
            sb.Append($"#EXTINF:{seconds},\n");
            sb.Append($"seg_{i:D5}.m4s\n");
        }

        sb.Append("#EXT-X-ENDLIST\n");
        return sb.ToString();
    }

    private static bool TryParseSegmentIndex(string fileName, out int index)
    {
        index = -1;
        if (fileName.StartsWith("seg_", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".m4s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(fileName[4..^4], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            index = parsed;
            return true;
        }

        return false;
    }

    private void StartPrebuild(int from)
    {
        for (var i = from + 1; i <= from + 3 && i < _videoSegments.Count; i++)
        {
            var segment = i;
            _ = Task.Run(() => BuildSegmentAsync(segment));
        }
    }

    private async Task BuildSegmentAsync(int segment)
    {
        if (segment < 0 || segment >= _videoSegments.Count)
            return;

        var segmentPath = Path.Combine(_dir, $"seg_{segment:D5}.m4s");
        var initPath = Path.Combine(_dir, "init.mp4");

        if (File.Exists(segmentPath) && (segment > 0 || File.Exists(initPath)))
            return;

        var lazy = _building.GetOrAdd(segment, s => new(
            () => BuildSegmentCoreAsync(s, segmentPath, initPath),
            LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            await lazy.Value;
        }
        finally
        {
            if (!File.Exists(segmentPath))
                _building.TryRemove(segment, out _);
        }
    }

    private async Task BuildSegmentCoreAsync(int segment, string segmentPath, string initPath)
    {
        if (_cachedVideoInit == null && !string.IsNullOrEmpty(_videoInitUrl))
            _cachedVideoInit = await FetchBytesAsync(_httpClient, _videoInitUrl, _cookies);
        if (_cachedAudioInit == null && !string.IsNullOrEmpty(_audioInitUrl))
            _cachedAudioInit = await FetchBytesAsync(_httpClient, _audioInitUrl, _cookies);

        var videoInit = _cachedVideoInit ?? [];
        var audioInit = _cachedAudioInit ?? [];

        var videoSeg = _videoSegments[segment];
        var videoKeyUrl = videoSeg.KeyUrl ?? _videoKeyUrl;
        if (_cachedVideoKey == null && !string.IsNullOrEmpty(videoKeyUrl))
            _cachedVideoKey = await FetchBytesAsync(_httpClient, videoKeyUrl, _cookies);

        var audioSeg = segment < _audioSegments.Count ? _audioSegments[segment] : null;
        var audioKeyUrl = audioSeg?.KeyUrl ?? _audioKeyUrl;
        if (_cachedAudioKey == null && !string.IsNullOrEmpty(audioKeyUrl))
            _cachedAudioKey = await FetchBytesAsync(_httpClient, audioKeyUrl, _cookies);

        var rawVideoBytes = await FetchBytesAsync(_httpClient, videoSeg.Url, _cookies);
        var videoBytes = rawVideoBytes;
        if (_cachedVideoKey is { Length: > 0 })
        {
            var iv = ParseIv(videoSeg.KeyIv ?? _videoKeyIv, segment + 1);
            videoBytes = DecryptAes128(rawVideoBytes, _cachedVideoKey, iv);
        }

        byte[]? audioBytes = null;
        if (audioSeg != null)
        {
            var rawAudioBytes = await FetchBytesAsync(_httpClient, audioSeg.Url, _cookies);
            audioBytes = rawAudioBytes;
            if (_cachedAudioKey is { Length: > 0 })
            {
                var iv = ParseIv(audioSeg.KeyIv ?? _audioKeyIv, segment + 1);
                audioBytes = DecryptAes128(rawAudioBytes, _cachedAudioKey, iv);
            }
        }

        var startMs = _startMs[segment];
        var audioList = audioBytes != null
            ? new List<byte[]>
            {
                audioBytes
            }
            : (IReadOnlyList<byte[]>)[];

        await _muxer.MuxDirectSegmentAsync(videoInit, videoBytes, audioInit, audioList, startMs, segment + 1, segmentPath,
            initPath);
    }

    internal static byte[] ParseIv(string? ivHex, int sequenceNumber)
    {
        if (!string.IsNullOrEmpty(ivHex))
        {
            var hex = ivHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? ivHex[2..] : ivHex;
            if (hex.Length == 32)
                try
                {
                    return Convert.FromHexString(hex);
                }
                catch
                {
                    // fallback
                }
        }

        var iv = new byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(iv.AsSpan(8), (ulong)sequenceNumber);
        return iv;
    }

    internal static byte[] DecryptAes128(byte[] cipherText, byte[] key, byte[] iv)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
        }
        catch
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
        }
    }

    internal static async Task<string> FetchTextAsync(HttpClient client, string url, Dictionary<string, string> cookies,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", NicoVideoApiService.UserAgent);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Referer", "https://www.nicovideo.jp/");
        req.Headers.TryAddWithoutValidation("Origin", "https://www.nicovideo.jp");

        var cookieHeader = string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));
        if (!string.IsNullOrEmpty(cookieHeader))
            req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        using var res = await client.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(ct);
    }

    internal static async Task<byte[]> FetchBytesAsync(HttpClient client, string url, Dictionary<string, string> cookies,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", NicoVideoApiService.UserAgent);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Referer", "https://www.nicovideo.jp/");
        req.Headers.TryAddWithoutValidation("Origin", "https://www.nicovideo.jp");

        var cookieHeader = string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));
        if (!string.IsNullOrEmpty(cookieHeader))
            req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        using var res = await client.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsByteArrayAsync(ct);
    }

    internal static (string? VideoUrl, string? AudioUrl) ParseMasterPlaylist(string masterText, string masterBaseUrl)
    {
        string? bestVideoUrl = null;
        var maxBandwidth = -1L;

        var audioMatch = AudioMediaRegex().Match(masterText);
        var audioUrl = audioMatch.Success ? ResolveUrl(masterBaseUrl, audioMatch.Groups[1].Value) : null;

        var lines = masterText.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.OrdinalIgnoreCase)) continue;
            var match = StreamInfRegex().Match(line);
            var bandwidth = 0L;
            if (match.Success && long.TryParse(match.Groups[2].Value, out var bw))
                bandwidth = bw;

            for (var j = i + 1; j < lines.Length; j++)
            {
                var nextLine = lines[j].Trim();
                if (string.IsNullOrEmpty(nextLine) || nextLine.StartsWith('#')) continue;

                var candidateUrl = ResolveUrl(masterBaseUrl, nextLine);
                if (bandwidth > maxBandwidth)
                {
                    maxBandwidth = bandwidth;
                    bestVideoUrl = candidateUrl;
                }

                break;
            }
        }

        return (bestVideoUrl, audioUrl);
    }

    internal static void ParseVariantPlaylist(string playlistText, string playlistBaseUrl, out string? initUrl,
        out string? defaultKeyUrl, out string? defaultKeyIv, List<NicoSegmentItem> segments)
    {
        initUrl = null;
        defaultKeyUrl = null;
        defaultKeyIv = null;

        var mapMatch = MapUriRegex().Match(playlistText);
        if (mapMatch.Success)
            initUrl = ResolveUrl(playlistBaseUrl, mapMatch.Groups[1].Value);

        var keyMatch = KeyRegex().Match(playlistText);
        if (keyMatch.Success)
        {
            defaultKeyUrl = ResolveUrl(playlistBaseUrl, keyMatch.Groups[1].Value);
            if (keyMatch.Groups.Count > 2 && keyMatch.Groups[2].Success)
                defaultKeyIv = keyMatch.Groups[2].Value;
        }

        var lines = playlistText.Split('\n');
        double? currentDuration = null;
        var currentKeyUrl = defaultKeyUrl;
        var currentKeyIv = defaultKeyIv;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("#EXT-X-KEY:", StringComparison.OrdinalIgnoreCase))
            {
                var km = KeyRegex().Match(line);
                if (km.Success)
                {
                    currentKeyUrl = ResolveUrl(playlistBaseUrl, km.Groups[1].Value);
                    currentKeyIv = km.Groups.Count > 2 && km.Groups[2].Success ? km.Groups[2].Value : defaultKeyIv;
                }

                continue;
            }

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                var infMatch = ExtInfRegex().Match(line);
                if (infMatch.Success && double.TryParse(infMatch.Groups[1].Value, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var d))
                    currentDuration = d;
                continue;
            }

            if (line.StartsWith('#'))
                continue;

            if (currentDuration.HasValue)
            {
                var segmentUrl = ResolveUrl(playlistBaseUrl, line);
                segments.Add(new()
                {
                    Url = segmentUrl,
                    Duration = currentDuration.Value,
                    KeyUrl = currentKeyUrl,
                    KeyIv = currentKeyIv
                });
                currentDuration = null;
            }
        }
    }

    internal static string ResolveUrl(string baseUrl, string relativeOrAbsolute)
    {
        if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var absUri))
            return absUri.ToString();
        if (Uri.TryCreate(new(baseUrl), relativeOrAbsolute, out var resolvedUri))
            return resolvedUri.ToString();
        return relativeOrAbsolute;
    }
}