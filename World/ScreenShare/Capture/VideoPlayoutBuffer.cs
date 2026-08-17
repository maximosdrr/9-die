using System.Collections.Generic;
using Godot;

/// <summary>
/// Smooths out arrival jitter (network timing variance, uneven encode time) on the receiving
/// side by holding each incoming frame for a fixed minimum delay before releasing it for
/// display — the same trade-off (a little latency for steady playback) real live-streaming
/// services make. Runs entirely on the main thread alongside RPC handling and _Process, so it
/// doesn't need to be thread-safe.
/// </summary>
public sealed class VideoPlayoutBuffer
{
    private const ulong TargetLatencyMs = 50;
    private const ulong MaxLatencyMs = TargetLatencyMs * 3;
    private const int MaxBufferedFrames = 4;

    private readonly Queue<(ulong ArrivalMs, byte[] Bytes)> _frames = new();

    internal int BufferedFrameCount => _frames.Count;

    public void Enqueue(byte[] encodedBytes)
    {
        while (_frames.Count >= MaxBufferedFrames)
            _frames.Dequeue();

        _frames.Enqueue((Time.GetTicksMsec(), encodedBytes));
    }

    /// <summary>
    /// Releases a frame once it has waited at least TargetLatencyMs since it arrived. If the
    /// caller falls behind and several frames have already crossed that threshold by the time
    /// this is checked, only the newest of them is returned (the rest are dropped) so playback
    /// lag doesn't keep compounding.
    /// </summary>
    public bool TryDequeueDue(out byte[] frame)
    {
        frame = null;
        var now = Time.GetTicksMsec();

        // Hard catch-up: a sustained slow patch (network or encode) can pile frames up faster
        // than TargetLatencyMs alone would drain them — if the oldest frame is already older
        // than MaxLatencyMs, drop the backlog outright instead of letting delay grow unbounded.
        while (_frames.Count > 0 && now - _frames.Peek().ArrivalMs > MaxLatencyMs)
            _frames.Dequeue();

        var got = false;
        while (_frames.Count > 0 && now - _frames.Peek().ArrivalMs >= TargetLatencyMs)
        {
            frame = _frames.Dequeue().Bytes;
            got = true;
        }

        return got;
    }

    public void Clear()
    {
        _frames.Clear();
    }
}
