namespace VRCVideoCacher.Services.Sabr;

/// <summary>One live fragment as the server described it.</summary>
internal readonly record struct LiveFragment(long Sequence, long StartMs, long DurationMs);

/// <summary>
/// The live equivalent of <see cref="SegmentIndex"/> — and deliberately a different type, because a
/// live timeline breaks every assumption the VOD index is built on:
///
/// <list type="bullet">
/// <item>It is <b>discovered</b>, not published. There is no <c>sidx</c>/<c>Cues</c> on a livestream
///   (measured), so the only way to learn a fragment's duration is to receive it.</item>
/// <item>It is keyed by the server's <b>absolute sequence number</b> — observed around 1,924,000 on a
///   real broadcast — not a zero-based index. <see cref="SegmentIndex.StartMsOf"/>'s prefix-sum-from-zero
///   is meaningless here.</item>
/// <item>It <b>slides</b>. Old fragments are evicted as the window moves, so the first sequence we can
///   still serve rises over time.</item>
/// </list>
///
/// Thread-safe: the fill loop appends while HLS requests read.
/// </summary>
internal sealed class LiveTimeline
{
    private readonly object _gate = new();
    private readonly SortedDictionary<long, LiveFragment> _fragments = [];
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Oldest sequence still held, or 0 when empty.</summary>
    public long FirstSequence
    {
        get { lock (_gate) return _fragments.Count == 0 ? 0 : _fragments.Keys.First(); }
    }

    /// <summary>Newest sequence held, or 0 when empty.</summary>
    public long LastSequence
    {
        get { lock (_gate) return _fragments.Count == 0 ? 0 : _fragments.Keys.Last(); }
    }

    public int Count
    {
        get { lock (_gate) return _fragments.Count; }
    }

    /// <summary>Records a fragment. Re-delivering one already held is ignored, not an error.</summary>
    public void Append(long sequence, long startMs, long durationMs)
    {
        TaskCompletionSource toSignal;
        lock (_gate)
        {
            if (!_fragments.TryAdd(sequence, new LiveFragment(sequence, startMs, durationMs)))
                return;
            toSignal = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        toSignal.TrySetResult();
    }

    public bool TryGet(long sequence, out LiveFragment fragment)
    {
        lock (_gate) return _fragments.TryGetValue(sequence, out fragment);
    }

    /// <summary>
    /// The newest <paramref name="count"/> fragments, oldest first — what the HLS playlist advertises.
    /// Deliberately excludes the very newest fragment held: a segment is only playable once BOTH tracks
    /// cover it, and audio/video arrive independently.
    /// </summary>
    public IReadOnlyList<LiveFragment> Window(int count)
    {
        lock (_gate)
        {
            if (_fragments.Count == 0)
                return [];
            return _fragments.Values.TakeLast(count).ToList();
        }
    }

    /// <summary>Drops everything older than <paramref name="sequence"/>. This is what bounds the disk.</summary>
    public IReadOnlyList<long> EvictBefore(long sequence)
    {
        lock (_gate)
        {
            var stale = _fragments.Keys.Where(k => k < sequence).ToList();
            foreach (var key in stale)
                _fragments.Remove(key);
            return stale;
        }
    }

    /// <summary>
    /// Waits until <paramref name="sequence"/> is available. A live player legitimately asks for a
    /// segment moments before it is broadcast, so waiting is correct where a VOD session would seek.
    /// </summary>
    public async Task<bool> WaitForAsync(long sequence, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                if (_fragments.ContainsKey(sequence))
                    return true;
                // Already slid past — it will never arrive, so do not wait out the timeout.
                if (_fragments.Count > 0 && sequence < _fragments.Keys.First())
                    return false;
                changed = _changed.Task;
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return false;

            await Task.WhenAny(changed, Task.Delay(remaining, ct));
            ct.ThrowIfCancellationRequested();
        }
    }
}
