using System.Text.Json.Serialization;

namespace VRCVideoCacher.Services.Nico;

public class NicoWatchPageData
{
    [JsonPropertyName("data")] public NicoWatchData? Data { get; set; }
}

public class NicoWatchData
{
    [JsonPropertyName("response")] public NicoWatchResponse? Response { get; set; }
}

public class NicoWatchResponse
{
    [JsonPropertyName("video")] public NicoWatchVideo? Video { get; set; }
    [JsonPropertyName("tag")] public NicoWatchTagContainer? Tag { get; set; }
    [JsonPropertyName("media")] public NicoWatchMedia? Media { get; set; }
    [JsonPropertyName("client")] public NicoWatchClient? Client { get; set; }
    [JsonPropertyName("owner")] public NicoWatchOwner? Owner { get; set; }
}

public class NicoWatchOwner
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("nickname")] public string? Nickname { get; set; }
    [JsonPropertyName("iconUrl")] public string? IconUrl { get; set; }
}

public class NicoWatchVideo
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("duration")] public long? Duration { get; set; }
    [JsonPropertyName("thumbnail")] public NicoWatchThumbnail? Thumbnail { get; set; }
    [JsonPropertyName("count")] public NicoWatchCount? Count { get; set; }
}

public class NicoWatchThumbnail
{
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("player")] public string? Player { get; set; }
}

public class NicoWatchCount
{
    [JsonPropertyName("view")] public long? View { get; set; }
    [JsonPropertyName("comment")] public long? Comment { get; set; }
    [JsonPropertyName("mylist")] public long? MyList { get; set; }
    [JsonPropertyName("like")] public long? Like { get; set; }
}

public class NicoWatchTagContainer
{
    [JsonPropertyName("items")] public List<NicoWatchTagItem>? Items { get; set; }
}

public class NicoWatchTagItem
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public class NicoWatchMedia
{
    [JsonPropertyName("domand")] public NicoWatchDomand? Domand { get; set; }
}

public class NicoWatchDomand
{
    [JsonPropertyName("accessRightKey")] public string? AccessRightKey { get; set; }
    [JsonPropertyName("audios")] public List<NicoWatchAudioVideoItem>? Audios { get; set; }
    [JsonPropertyName("videos")] public List<NicoWatchAudioVideoItem>? Videos { get; set; }
}

public class NicoWatchAudioVideoItem
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("isAvailable")] public bool? IsAvailable { get; set; }
}

public class NicoWatchClient
{
    [JsonPropertyName("watchTrackId")] public string? WatchTrackId { get; set; }
    [JsonPropertyName("nicosid")] public string? NicosId { get; set; }
}

public class NicoLivePageData
{
    [JsonPropertyName("site")] public NicoLiveSite? Site { get; set; }
    [JsonPropertyName("program")] public NicoLiveProgram? Program { get; set; }
}

public class NicoLiveSite
{
    [JsonPropertyName("locale")] public string? Locale { get; set; }
    [JsonPropertyName("serverTime")] public long? ServerTime { get; set; }
    [JsonPropertyName("frontendVersion")] public string? FrontendVersion { get; set; }
    [JsonPropertyName("frontendId")] public int? FrontendId { get; set; }
    [JsonPropertyName("relive")] public NicoLiveRelive? Relive { get; set; }
}

public class NicoLiveRelive
{
    [JsonPropertyName("apiBaseUrl")] public string? ApiBaseUrl { get; set; }

    [JsonPropertyName("channelApiBaseUrl")]
    public string? ChannelApiBaseUrl { get; set; }

    [JsonPropertyName("webSocketUrl")] public string? WebSocketUrl { get; set; }
    [JsonPropertyName("csrfToken")] public string? CsrfToken { get; set; }
    [JsonPropertyName("audienceToken")] public string? AudienceToken { get; set; }
}

public class NicoLiveProgram
{
    [JsonPropertyName("nicoliveProgramId")]
    public string? NicoliveProgramId { get; set; }

    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("openTime")] public long? OpenTime { get; set; }
    [JsonPropertyName("beginTime")] public long? BeginTime { get; set; }
    [JsonPropertyName("endTime")] public long? EndTime { get; set; }
    [JsonPropertyName("supplier")] public NicoLiveSupplier? Supplier { get; set; }
    [JsonPropertyName("thumbnail")] public NicoLiveThumbnailContainer? Thumbnail { get; set; }
    [JsonPropertyName("tag")] public NicoLiveTagContainer? Tag { get; set; }
    [JsonPropertyName("stream")] public NicoLiveStreamInfo? Stream { get; set; }
    [JsonPropertyName("statistics")] public NicoLiveStatistics? Statistics { get; set; }
}

public class NicoLiveSupplier
{
    [JsonPropertyName("supplierType")] public string? SupplierType { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("pageUrl")] public string? PageUrl { get; set; }
}

public class NicoLiveThumbnailContainer
{
    [JsonPropertyName("small")] public string? Small { get; set; }
    [JsonPropertyName("huge")] public NicoLiveThumbnailHuge? Huge { get; set; }
}

public class NicoLiveThumbnailHuge
{
    [JsonPropertyName("s1920x1080")] public string? S1920X1080 { get; set; }
    [JsonPropertyName("s1280x720")] public string? S1280X720 { get; set; }
    [JsonPropertyName("s640x360")] public string? S640X360 { get; set; }
    [JsonPropertyName("s352x198")] public string? S352X198 { get; set; }
}

public class NicoLiveTagContainer
{
    [JsonPropertyName("list")] public List<NicoLiveTagItem>? List { get; set; }
}

public class NicoLiveTagItem
{
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("isLocked")] public bool? IsLocked { get; set; }
}

public class NicoLiveStreamInfo
{
    [JsonPropertyName("maxQuality")] public string? MaxQuality { get; set; }
}

public class NicoLiveStatistics
{
    [JsonPropertyName("watchCount")] public long? WatchCount { get; set; }
    [JsonPropertyName("commentCount")] public long? CommentCount { get; set; }
}

public class NicoNvApiAccessRightsRequest
{
    [JsonPropertyName("outputs")] public List<string[]> Outputs { get; set; } = [];
}

public class NicoNvApiAccessRightsResponse
{
    [JsonPropertyName("data")] public NicoNvApiAccessRightsData? Data { get; set; }
}

public class NicoNvApiAccessRightsData
{
    [JsonPropertyName("contentUrl")] public string? ContentUrl { get; set; }
}

public class NicoWsStartWatchingMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "startWatching";
    [JsonPropertyName("data")] public NicoWsStartWatchingData Data { get; set; } = new();
}

public class NicoWsStartWatchingData
{
    [JsonPropertyName("reconnect")] public bool Reconnect { get; set; }
    [JsonPropertyName("room")] public NicoWsRoom Room { get; set; } = new();
    [JsonPropertyName("stream")] public NicoWsStream Stream { get; set; } = new();
}

public class NicoWsRoom
{
    [JsonPropertyName("protocol")] public string Protocol { get; set; } = "webSocket";
    [JsonPropertyName("commentable")] public bool Commentable { get; set; } = true;
}

public class NicoWsStream
{
    [JsonPropertyName("accessRightMethod")]
    public string AccessRightMethod { get; set; } = "single_cookie";

    [JsonPropertyName("chasePlay")] public bool ChasePlay { get; set; }
    [JsonPropertyName("latency")] public string Latency { get; set; } = "high";
    [JsonPropertyName("protocol")] public string Protocol { get; set; } = "hls";
    [JsonPropertyName("quality")] public string Quality { get; set; } = "abr";
}

public class NicoWsSimpleMessage
{
    [JsonPropertyName("type")] public string? Type { get; set; }
}

public class NicoWsStreamMessage
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("data")] public NicoWsStreamData? Data { get; set; }
}

public class NicoWsStreamData
{
    [JsonPropertyName("uri")] public string? Uri { get; set; }
    [JsonPropertyName("quality")] public string? Quality { get; set; }
    [JsonPropertyName("protocol")] public string? Protocol { get; set; }
    [JsonPropertyName("cookies")] public List<NicoWsCookieItem>? Cookies { get; set; }
}

public class NicoWsCookieItem
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
}

[JsonSourceGenerationOptions(
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NicoWatchPageData))]
[JsonSerializable(typeof(NicoLivePageData))]
[JsonSerializable(typeof(NicoNvApiAccessRightsRequest))]
[JsonSerializable(typeof(NicoNvApiAccessRightsResponse))]
[JsonSerializable(typeof(NicoWsStartWatchingMessage))]
[JsonSerializable(typeof(NicoWsSimpleMessage))]
[JsonSerializable(typeof(NicoWsStreamMessage))]
internal partial class NicoJsonContext : JsonSerializerContext
{
}