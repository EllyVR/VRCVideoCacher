using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Web;
using Serilog;

namespace VRCVideoCacher.Services.Nico;

internal sealed partial class NicoLiveSession : INicoSession
{
    private const int WindowSegments = 4;
    private const int RetainSegments = 12;
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<long, NicoSegmentItem> _audioSegments = new();
    private readonly SemaphoreSlim _buildGate = new(1, 1);

    private readonly ConcurrentDictionary<long, Lazy<Task>> _building = new();
    private readonly ConcurrentDictionary<string, byte[]> _cachedKeys = new();
    private readonly Dictionary<string, string> _cookies = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _dir;
    private readonly HttpClient _httpClient;

    private readonly string _liveId;
    private readonly NicoLiveResult _liveResult;
    private readonly ILogger _log;
    private readonly NicoSegmentMuxer _muxer;
    private readonly TaskCompletionSource<string> _streamUriTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<long, NicoSegmentItem> _videoSegments = new();
    private readonly ClientWebSocket _ws = new();
    private string? _audioInitUrl;
    private string? _audioVariantUrl;
    private byte[]? _cachedAudioInit;

    private byte[]? _cachedVideoInit;
    private volatile bool _isEnded;
    private int _targetDuration = 2;
    private string? _videoInitUrl;
    private string? _videoVariantUrl;

    private NicoLiveSession(string liveId, string dir, NicoLiveResult liveResult, HttpClient httpClient,
        NicoSegmentMuxer muxer, ILogger log)
    {
        _liveId = liveId;
        _dir = dir;
        _liveResult = liveResult;
        _httpClient = httpClient;
        _muxer = muxer;
        _log = log;

        foreach (var kv in liveResult.Cookies)
            _cookies[kv.Key] = kv.Value;
    }

    public string PlaybackUrl => $"{ConfigManager.Config.YtdlpWebServerUrl.TrimEnd('/')}/nico/{_liveId}/index.m3u8";
    public DateTime LastAccess { get; private set; } = DateTime.UtcNow;

    public void Touch() => LastAccess = DateTime.UtcNow;

    public async Task EnsureAsync(string fileName)
    {
        Touch();

        if (fileName.Equals("index.m3u8", StringComparison.OrdinalIgnoreCase))
        {
            await WritePlaylistAsync();
            return;
        }

        if (fileName.Equals("init.mp4", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(Path.Combine(_dir, "init.mp4")))
            {
                var oldest = _videoSegments.Keys.DefaultIfEmpty(0).Min();
                await BuildSegmentAsync(oldest);
            }

            return;
        }

        if (TryParseSegmentSequence(fileName, out var sequence))
        {
            await BuildSegmentAsync(sequence);
            StartPrebuild(sequence);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();

        try
        {
            _ws.Abort();
        }
        catch
        {
            // ignore
        }

        try
        {
            _ws.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            _buildGate.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            _cts.Dispose();
        }
        catch
        {
            // ignore
        }

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

    [GeneratedRegex(@"#EXT-X-TARGETDURATION:(\d+)", RegexOptions.Compiled)]
    private static partial Regex TargetDurationRegex();

    [GeneratedRegex(@"#EXT-X-MEDIA-SEQUENCE:(\d+)", RegexOptions.Compiled)]
    private static partial Regex MediaSequenceRegex();

    [GeneratedRegex(@"#EXT-X-MAP:URI=""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex MapUriRegex();

    [GeneratedRegex(@"#EXT-X-KEY:METHOD=AES-128,URI=""([^""]+)""(?:,IV=([0-9a-fA-FxX]+))?", RegexOptions.Compiled)]
    private static partial Regex KeyRegex();

    [GeneratedRegex(@"#EXTINF:([0-9.]+),", RegexOptions.Compiled)]
    private static partial Regex ExtInfRegex();

    public static async Task<NicoLiveSession?> StartAsync(string liveId, NicoLiveResult liveResult, string rootDir,
        HttpClient httpClient, NicoSegmentMuxer muxer, ILogger log)
    {
        var dir = Path.Combine(rootDir, liveId);
        if (Directory.Exists(dir))
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // ignore
            }

        Directory.CreateDirectory(dir);

        var session = new NicoLiveSession(liveId, dir, liveResult, httpClient, muxer, log);

        try
        {
            using var startCts = new CancellationTokenSource(StartTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(startCts.Token, session._cts.Token);
            var ct = linkedCts.Token;

            var wsUrl = liveResult.WebSocketUrl;
            if (string.IsNullOrEmpty(wsUrl))
            {
                log.Warning("NicoLive {LiveId}: No WebSocketUrl present in live result", liveId);
                session.Dispose();
                return null;
            }

            if (liveResult.FrontendId.HasValue)
            {
                var uriBuilder = new UriBuilder(new Uri(wsUrl));
                var query = HttpUtility.ParseQueryString(uriBuilder.Query);
                query["frontend_id"] = liveResult.FrontendId.Value.ToString();
                uriBuilder.Query = query.ToString()!;
                wsUrl = uriBuilder.ToString();
            }

            session._ws.Options.SetRequestHeader("Origin", "https://live.nicovideo.jp");
            session._ws.Options.SetRequestHeader("User-Agent", NicoVideoApiService.UserAgent);

            await session._ws.ConnectAsync(new(wsUrl), ct);

            var startWatching = new NicoWsStartWatchingMessage
            {
                Type = "startWatching",
                Data = new()
                {
                    Reconnect = false,
                    Room = new()
                    {
                        Protocol = "webSocket",
                        Commentable = true
                    },
                    Stream = new()
                    {
                        AccessRightMethod = "single_cookie",
                        ChasePlay = false,
                        Latency = "high",
                        Protocol = "hls",
                        Quality = "abr"
                    }
                }
            };

            await SendWsMessageAsync(session._ws, startWatching, NicoJsonContext.Default.NicoWsStartWatchingMessage, ct);

            _ = session.WsLoopAsync(session._cts.Token);

            var streamUri = await session._streamUriTcs.Task.WaitAsync(ct);
            if (string.IsNullOrEmpty(streamUri))
            {
                log.Warning("NicoLive {LiveId}: Failed to obtain stream uri from WebSocket", liveId);
                session.Dispose();
                return null;
            }

            Dictionary<string, string> cookiesCopy;
            lock (session._cookies)
                cookiesCopy = new(session._cookies);

            var masterText = await NicoHlsSession.FetchTextAsync(httpClient, streamUri, cookiesCopy, ct);
            var (bestVideoUrl, audioUrl) = NicoHlsSession.ParseMasterPlaylist(masterText, streamUri);

            session._videoVariantUrl = bestVideoUrl ?? streamUri;
            session._audioVariantUrl = audioUrl;

            _ = session.PollLoopAsync(session._cts.Token);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (DateTime.UtcNow < deadline && !session._isEnded)
            {
                if (session._videoSegments.Count >= 2)
                    break;
                await Task.Delay(200, ct);
            }

            if (session._videoSegments.Count == 0)
            {
                log.Warning("NicoLive {LiveId}: No media segments discovered within timeout", liveId);
                session.Dispose();
                return null;
            }

            var firstSeq = session._videoSegments.Keys.Min();
            await session.BuildSegmentAsync(firstSeq);
            await session.WritePlaylistAsync();

            log.Information("NicoLive ready for {LiveId}: initial sequence {Seq}, target duration {Duration}s",
                liveId, firstSeq, session._targetDuration);

            return session;
        }
        catch (Exception ex)
        {
            log.Error(ex, "NicoLive {LiveId}: Exception during session start", liveId);
            session.Dispose();
            return null;
        }
    }

    private static async Task SendWsMessageAsync<T>(ClientWebSocket ws, T value,
        JsonTypeInfo<T> jsonTypeInfo, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, jsonTypeInfo);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, ct);
    }

    private async Task WsLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            try
            {
                using var ms = new MemoryStream();
                ValueWebSocketReceiveResult receiveResult;
                do
                {
                    receiveResult = await _ws.ReceiveAsync(buffer.AsMemory(), ct);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        _log.Information("NicoLive {LiveId}: WebSocket received Close frame", _liveId);
                        _isEnded = true;
                        return;
                    }

                    ms.Write(buffer, 0, receiveResult.Count);
                } while (!receiveResult.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp)) continue;
                var type = typeProp.GetString();

                switch (type)
                {
                    case "stream":
                    {
                        if (root.TryGetProperty("data", out var data))
                        {
                            if (data.TryGetProperty("uri", out var uriProp))
                            {
                                var uri = uriProp.GetString();
                                if (!string.IsNullOrEmpty(uri))
                                    _streamUriTcs.TrySetResult(uri);
                            }

                            if (data.TryGetProperty("cookies", out var cookiesProp) &&
                                cookiesProp.ValueKind == JsonValueKind.Array)
                                foreach (var cookie in cookiesProp.EnumerateArray())
                                {
                                    var name = cookie.TryGetProperty("name", out var n) ? n.GetString() : null;
                                    var val = cookie.TryGetProperty("value", out var v) ? v.GetString() : null;
                                    if (!string.IsNullOrEmpty(name) && val != null)
                                        lock (_cookies)
                                            _cookies[name] = val;
                                }
                        }

                        break;
                    }

                    case "ping":
                    {
                        await SendWsMessageAsync(_ws, new()
                            {
                                Type = "pong"
                            },
                            NicoJsonContext.Default.NicoWsSimpleMessage, ct);
                        await SendWsMessageAsync(_ws, new()
                            {
                                Type = "keepSeat"
                            },
                            NicoJsonContext.Default.NicoWsSimpleMessage, ct);
                        break;
                    }

                    case "serverKeep":
                    {
                        await SendWsMessageAsync(_ws, new()
                            {
                                Type = "pong"
                            },
                            NicoJsonContext.Default.NicoWsSimpleMessage, ct);
                        break;
                    }

                    case "disconnect":
                    case "error":
                    {
                        _log.Warning("NicoLive {LiveId} WebSocket {Type}: {Json}", _liveId, type, json);
                        _isEnded = true;
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    _log.Error(ex, "NicoLive {LiveId}: WebSocket error in receive loop", _liveId);
                break;
            }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_isEnded)
        {
            try
            {
                await PollVariantPlaylistsAsync(ct);
                Evict();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "NicoLive {LiveId}: Error polling HLS playlist", _liveId);
            }

            var delayMs = Math.Max(1000, _targetDuration * 1000 / 2);
            await Task.Delay(delayMs, ct);
        }
    }

    private async Task PollVariantPlaylistsAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_videoVariantUrl)) return;

        Dictionary<string, string> cookiesCopy;
        lock (_cookies)
            cookiesCopy = new(_cookies);

        var videoText = await NicoHlsSession.FetchTextAsync(_httpClient, _videoVariantUrl, cookiesCopy, ct);
        ParseLiveVariant(videoText, _videoVariantUrl, out var videoInit, out var videoTargetDuration,
            out var videoMediaSeq, out var isEnded, out var videoItems);

        if (!string.IsNullOrEmpty(videoInit))
            _videoInitUrl = videoInit;
        if (videoTargetDuration > 0)
            _targetDuration = videoTargetDuration;
        if (isEnded)
            _isEnded = true;

        for (var i = 0; i < videoItems.Count; i++)
        {
            var seq = videoMediaSeq + i;
            _videoSegments.TryAdd(seq, videoItems[i]);
        }

        if (!string.IsNullOrEmpty(_audioVariantUrl))
        {
            var audioText = await NicoHlsSession.FetchTextAsync(_httpClient, _audioVariantUrl, cookiesCopy, ct);
            ParseLiveVariant(audioText, _audioVariantUrl, out var audioInit, out _, out var audioMediaSeq, out _,
                out var audioItems);

            if (!string.IsNullOrEmpty(audioInit))
                _audioInitUrl = audioInit;

            for (var i = 0; i < audioItems.Count; i++)
            {
                var seq = audioMediaSeq + i;
                _audioSegments.TryAdd(seq, audioItems[i]);
            }
        }
    }

    private static void ParseLiveVariant(string playlistText, string playlistBaseUrl, out string? initUrl,
        out int targetDuration, out long mediaSequence, out bool isEnded, out List<NicoSegmentItem> segments)
    {
        initUrl = null;
        targetDuration = 2;
        mediaSequence = 0;
        isEnded = false;
        segments = [];

        var targetDurMatch = TargetDurationRegex().Match(playlistText);
        if (targetDurMatch.Success && int.TryParse(targetDurMatch.Groups[1].Value, out var td))
            targetDuration = td;

        var mediaSeqMatch = MediaSequenceRegex().Match(playlistText);
        if (mediaSeqMatch.Success && long.TryParse(mediaSeqMatch.Groups[1].Value, out var ms))
            mediaSequence = ms;

        if (playlistText.Contains("#EXT-X-ENDLIST", StringComparison.OrdinalIgnoreCase))
            isEnded = true;

        var mapMatch = MapUriRegex().Match(playlistText);
        if (mapMatch.Success)
            initUrl = NicoHlsSession.ResolveUrl(playlistBaseUrl, mapMatch.Groups[1].Value);

        var keyMatch = KeyRegex().Match(playlistText);
        string? defaultKeyUrl = null;
        string? defaultKeyIv = null;
        if (keyMatch.Success)
        {
            defaultKeyUrl = NicoHlsSession.ResolveUrl(playlistBaseUrl, keyMatch.Groups[1].Value);
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
                    currentKeyUrl = NicoHlsSession.ResolveUrl(playlistBaseUrl, km.Groups[1].Value);
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
                var segmentUrl = NicoHlsSession.ResolveUrl(playlistBaseUrl, line);
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

    private static bool TryParseSegmentSequence(string fileName, out long sequence)
    {
        sequence = -1;
        if (fileName.StartsWith("seg_", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".m4s", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(fileName[4..^4], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            sequence = parsed;
            return true;
        }

        return false;
    }

    private void StartPrebuild(long from)
    {
        for (var i = from + 1; i <= from + 2; i++)
        {
            var seq = i;
            if (_videoSegments.ContainsKey(seq))
                _ = Task.Run(() => BuildSegmentAsync(seq));
        }
    }

    private async Task WritePlaylistAsync()
    {
        var maxSeq = _videoSegments.Keys.DefaultIfEmpty(-1).Max();
        if (maxSeq < 0) return;

        var window = _videoSegments.Keys
            .Where(s => s <= maxSeq)
            .OrderBy(s => s)
            .TakeLast(WindowSegments)
            .ToList();

        if (window.Count == 0) return;

        var firstSeq = window[0];
        var sb = new StringBuilder();
        sb.Append("#EXTM3U\n");
        sb.Append("#EXT-X-VERSION:7\n");
        sb.Append($"#EXT-X-TARGETDURATION:{_targetDuration}\n");
        sb.Append($"#EXT-X-MEDIA-SEQUENCE:{firstSeq}\n");
        sb.Append("#EXT-X-INDEPENDENT-SEGMENTS\n");
        sb.Append("#EXT-X-MAP:URI=\"init.mp4\"\n");

        foreach (var seq in window)
        {
            var seg = _videoSegments[seq];
            var seconds = seg.Duration > 0 ? seg.Duration.ToString("F6", CultureInfo.InvariantCulture) : "2.000000";
            sb.Append($"#EXTINF:{seconds},\n");
            sb.Append($"seg_{seq:D5}.m4s\n");
        }

        if (_isEnded)
            sb.Append("#EXT-X-ENDLIST\n");

        var playlistPath = Path.Combine(_dir, "index.m3u8");
        await File.WriteAllTextAsync(playlistPath, sb.ToString(), _cts.Token);
    }

    private async Task BuildSegmentAsync(long sequence)
    {
        var segmentPath = Path.Combine(_dir, $"seg_{sequence:D5}.m4s");
        var initPath = Path.Combine(_dir, "init.mp4");

        if (File.Exists(segmentPath) && File.Exists(initPath))
            return;

        var lazy = _building.GetOrAdd(sequence, s => new(
            () => BuildSegmentCoreAsync(s, segmentPath, initPath),
            LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            await lazy.Value;
        }
        finally
        {
            if (!File.Exists(segmentPath))
                _building.TryRemove(sequence, out _);
        }
    }

    private async Task BuildSegmentCoreAsync(long sequence, string segmentPath, string initPath)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!_videoSegments.TryGetValue(sequence, out _) && DateTime.UtcNow < deadline &&
               !_cts.IsCancellationRequested)
            await Task.Delay(100, _cts.Token);

        if (!_videoSegments.TryGetValue(sequence, out var videoSeg))
        {
            _log.Warning("NicoLive {LiveId}: Video segment {Seq} not available to build", _liveId, sequence);
            return;
        }

        Dictionary<string, string> cookiesCopy;
        lock (_cookies)
            cookiesCopy = new(_cookies);

        if (_cachedVideoInit == null && !string.IsNullOrEmpty(_videoInitUrl))
            _cachedVideoInit = await NicoHlsSession.FetchBytesAsync(_httpClient, _videoInitUrl, cookiesCopy, _cts.Token);
        if (_cachedAudioInit == null && !string.IsNullOrEmpty(_audioInitUrl))
            _cachedAudioInit = await NicoHlsSession.FetchBytesAsync(_httpClient, _audioInitUrl, cookiesCopy, _cts.Token);

        var videoInit = _cachedVideoInit ?? [];
        var audioInit = _cachedAudioInit ?? [];

        var videoBytes = await NicoHlsSession.FetchBytesAsync(_httpClient, videoSeg.Url, cookiesCopy, _cts.Token);
        if (!string.IsNullOrEmpty(videoSeg.KeyUrl))
        {
            var key = await GetKeyBytesAsync(videoSeg.KeyUrl, cookiesCopy, _cts.Token);
            var iv = NicoHlsSession.ParseIv(videoSeg.KeyIv, (int)sequence);
            videoBytes = NicoHlsSession.DecryptAes128(videoBytes, key, iv);
        }

        byte[]? audioBytes = null;
        if (_audioSegments.TryGetValue(sequence, out var audioSeg))
        {
            audioBytes = await NicoHlsSession.FetchBytesAsync(_httpClient, audioSeg.Url, cookiesCopy, _cts.Token);
            if (!string.IsNullOrEmpty(audioSeg.KeyUrl))
            {
                var key = await GetKeyBytesAsync(audioSeg.KeyUrl, cookiesCopy, _cts.Token);
                var iv = NicoHlsSession.ParseIv(audioSeg.KeyIv, (int)sequence);
                audioBytes = NicoHlsSession.DecryptAes128(audioBytes, key, iv);
            }
        }

        var audioList = audioBytes != null ? [audioBytes] : (IReadOnlyList<byte[]>)[];

        var startMs = sequence * _targetDuration * 1000;

        await _buildGate.WaitAsync(_cts.Token);
        try
        {
            if (File.Exists(segmentPath) && File.Exists(initPath))
                return;

            await _muxer.MuxDirectSegmentAsync(videoInit, videoBytes, audioInit, audioList, startMs, (int)sequence,
                segmentPath, initPath, _cts.Token);
        }
        finally
        {
            _buildGate.Release();
        }
    }

    private async Task<byte[]> GetKeyBytesAsync(string keyUrl, Dictionary<string, string> cookies, CancellationToken ct)
    {
        if (_cachedKeys.TryGetValue(keyUrl, out var key))
            return key;

        var bytes = await NicoHlsSession.FetchBytesAsync(_httpClient, keyUrl, cookies, ct);
        _cachedKeys[keyUrl] = bytes;
        return bytes;
    }

    private void Evict()
    {
        var maxSeq = _videoSegments.Keys.DefaultIfEmpty(-1).Max();
        if (maxSeq <= RetainSegments) return;

        var cutoff = maxSeq - RetainSegments;
        foreach (var seq in _videoSegments.Keys.Where(s => s < cutoff))
        {
            _videoSegments.TryRemove(seq, out _);
            _audioSegments.TryRemove(seq, out _);
            _building.TryRemove(seq, out _);

            var segFile = Path.Combine(_dir, $"seg_{seq:D5}.m4s");
            if (File.Exists(segFile))
                try
                {
                    File.Delete(segFile);
                }
                catch
                {
                    // ignore
                }
        }
    }
}