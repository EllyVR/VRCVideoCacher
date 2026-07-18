namespace VRCVideoCacher.Services.Sabr;

/// <summary>
/// What <see cref="Services.SabrRestreamService"/> needs from a session, regardless of whether it is a
/// finite VOD (<see cref="SabrHlsSession"/>) or a live broadcast (<see cref="SabrLiveSession"/>).
///
/// Kept deliberately small: everything else about the two differs, and anything wider would push VOD
/// concepts (a total duration, a complete fetch, a cached file) onto live, where none of them exist.
/// </summary>
internal interface ISabrSession : IDisposable
{
    /// <summary>The playlist URL handed to the game.</summary>
    string PlaybackUrl { get; }

    /// <summary>How long since a request last touched this session — what the idle reaper reads.</summary>
    TimeSpan IdleFor { get; }

    /// <summary>Marks the session as in use. HLS requests are the only liveness signal we get.</summary>
    void Touch();

    /// <summary>Materialises the requested file (playlist, init or segment) before it is served.</summary>
    Task EnsureAsync(string fileName);
}
