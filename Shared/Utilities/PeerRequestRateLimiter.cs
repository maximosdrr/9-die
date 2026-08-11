using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// A small, bounded fixed-window limiter for requests received from multiplayer peers.
///
/// Each instance represents one logical class of requests on one server-owned node. The caller
/// should consume a request immediately after reading the transport sender, before parsing or
/// validating its payload. Unknown peers cannot grow this object indefinitely: once the bounded
/// table is full, a new peer is admitted only when an expired entry can be reused.
/// </summary>
public sealed class PeerRequestRateLimiter
{
    private readonly Dictionary<int, PeerWindow> _windows;

    public PeerRequestRateLimiter(int requestLimit, ulong windowMilliseconds, int maxTrackedPeers)
    {
        if (requestLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestLimit));
        if (windowMilliseconds == 0)
            throw new ArgumentOutOfRangeException(nameof(windowMilliseconds));
        if (maxTrackedPeers <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTrackedPeers));

        RequestLimit = requestLimit;
        WindowMilliseconds = windowMilliseconds;
        MaxTrackedPeers = maxTrackedPeers;
        _windows = new Dictionary<int, PeerWindow>(maxTrackedPeers);
    }

    public int RequestLimit { get; }
    public ulong WindowMilliseconds { get; }
    public int MaxTrackedPeers { get; }
    public int TrackedPeerCount => _windows.Count;

    /// <summary>Consumes one request using Godot's monotonic clock.</summary>
    public bool TryConsume(int peerId) => TryConsume(peerId, Time.GetTicksMsec());

    /// <summary>
    /// Consumes one request at a supplied monotonic timestamp. Supplying time makes rollover and
    /// capacity behaviour deterministic in tests without sleeping or touching the wall clock.
    /// </summary>
    public bool TryConsume(int peerId, ulong nowMilliseconds)
    {
        if (_windows.TryGetValue(peerId, out var window))
        {
            if (IsExpired(window, nowMilliseconds))
            {
                window.StartedAtMilliseconds = nowMilliseconds;
                window.Consumed = 1;
                return true;
            }

            if (window.Consumed >= RequestLimit)
                return false;

            window.Consumed++;
            return true;
        }

        if (_windows.Count >= MaxTrackedPeers && !TryRemoveExpiredWindow(nowMilliseconds))
            return false;

        _windows.Add(peerId, new PeerWindow(nowMilliseconds));
        return true;
    }

    public void Clear() => _windows.Clear();

    private bool IsExpired(PeerWindow window, ulong nowMilliseconds)
    {
        // A backwards jump is treated as a fresh window. Time.GetTicksMsec is monotonic in
        // production; this also makes an unsigned counter rollover fail open for one request.
        return nowMilliseconds < window.StartedAtMilliseconds
            || nowMilliseconds - window.StartedAtMilliseconds >= WindowMilliseconds;
    }

    private bool TryRemoveExpiredWindow(ulong nowMilliseconds)
    {
        var found = false;
        var expiredPeerId = 0;

        foreach (var entry in _windows)
        {
            if (!IsExpired(entry.Value, nowMilliseconds))
                continue;

            found = true;
            expiredPeerId = entry.Key;
            break;
        }

        return found && _windows.Remove(expiredPeerId);
    }

    private sealed class PeerWindow
    {
        public PeerWindow(ulong startedAtMilliseconds)
        {
            StartedAtMilliseconds = startedAtMilliseconds;
            Consumed = 1;
        }

        public ulong StartedAtMilliseconds;
        public int Consumed;
    }
}
