namespace VRCVideoCacher.Services.Nico;

/// <summary>
///     What <see cref="NicoRestreamService" /> needs from a session, whether it is a
///     finite VOD (<see cref="NicoHlsSession" />) or a live broadcast (<see cref="NicoLiveSession" />).
/// </summary>
internal interface INicoSession : IDisposable
{
    /// <summary>The playlist URL handed to the client/player.</summary>
    string PlaybackUrl { get; }

    /// <summary>Timestamp of the last request touching this session.</summary>
    DateTime LastAccess { get; }

    /// <summary>Marks the session as recently accessed.</summary>
    void Touch();

    /// <summary>Materialises the requested file (playlist, init or segment) before it is served.</summary>
    Task EnsureAsync(string fileName);
}