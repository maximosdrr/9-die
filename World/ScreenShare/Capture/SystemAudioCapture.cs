using System;
using System.Collections.Generic;
using NAudio.Wave;

/// <summary>
/// Captures the system's audio output (WASAPI loopback) into a thread-safe queue of raw PCM
/// byte chunks. NAudio's capture callback fires on its own thread, so consumers must drain
/// TryDequeueChunk from the main/Godot thread instead of touching Godot APIs from the callback.
/// </summary>
public interface IAudioCaptureSource : IDisposable
{
    WaveFormat WaveFormat { get; }
    void Start();
    void Stop();
    bool TryDequeueChunk(out byte[] chunk);
}

public sealed class SystemAudioCapture : IAudioCaptureSource
{
    // Audio is live media: after a hitch, playing half a second of old sound is worse than
    // dropping it and resuming near the present. Keep only a short recovery window.
    private const double MaxBufferedDurationSeconds = 0.12;

    private readonly WasapiLoopbackCapture _capture;
    private readonly BoundedAudioChunkQueue _pcmChunks;
    private int _disposeStarted;

    public WaveFormat WaveFormat => _capture.WaveFormat;

    public SystemAudioCapture()
    {
        _capture = new WasapiLoopbackCapture();
        var maxBufferedBytes = Math.Max(
            _capture.WaveFormat.BlockAlign,
            (int)(_capture.WaveFormat.AverageBytesPerSecond * MaxBufferedDurationSeconds));
        _pcmChunks = new BoundedAudioChunkQueue(maxBufferedBytes, _capture.WaveFormat.BlockAlign);
        _capture.DataAvailable += OnDataAvailable;
    }

    public void Start() => _capture.StartRecording();

    public void Stop() => _capture.StopRecording();

    private void OnDataAvailable(object sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
            return;

        var buffer = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, buffer, 0, e.BytesRecorded);
        _pcmChunks.Enqueue(buffer);
    }

    public bool TryDequeueChunk(out byte[] chunk) => _pcmChunks.TryDequeue(out chunk);

    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        _pcmChunks.CompleteAndClear();
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
    }
}

/// <summary>
/// Keeps a strict byte-duration budget for captured PCM. When the producer outruns playback,
/// the oldest chunks are discarded so newly captured audio remains live instead of accumulating
/// latency and memory indefinitely.
/// </summary>
internal sealed class BoundedAudioChunkQueue
{
    private readonly object _gate = new();
    private readonly Queue<byte[]> _chunks = new();
    private readonly int _maxBufferedBytes;
    private readonly int _blockAlign;

    private int _bufferedBytes;
    private bool _accepting = true;

    internal BoundedAudioChunkQueue(int maxBufferedBytes, int blockAlign)
    {
        if (maxBufferedBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBufferedBytes));
        if (blockAlign <= 0)
            throw new ArgumentOutOfRangeException(nameof(blockAlign));

        _blockAlign = blockAlign;
        _maxBufferedBytes = Math.Max(blockAlign, maxBufferedBytes / blockAlign * blockAlign);
    }

    internal int BufferedBytes
    {
        get
        {
            lock (_gate)
                return _bufferedBytes;
        }
    }

    internal int ChunkCount
    {
        get
        {
            lock (_gate)
                return _chunks.Count;
        }
    }

    internal bool Enqueue(byte[] chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (chunk.Length == 0)
            return false;

        lock (_gate)
        {
            if (!_accepting)
                return false;

            var retained = TrimToBudget(chunk);
            while (_bufferedBytes + retained.Length > _maxBufferedBytes && _chunks.Count > 0)
                _bufferedBytes -= _chunks.Dequeue().Length;

            _chunks.Enqueue(retained);
            _bufferedBytes += retained.Length;
            return true;
        }
    }

    internal bool TryDequeue(out byte[] chunk)
    {
        lock (_gate)
        {
            if (_chunks.Count == 0)
            {
                chunk = null;
                return false;
            }

            chunk = _chunks.Dequeue();
            _bufferedBytes -= chunk.Length;
            return true;
        }
    }

    internal void CompleteAndClear()
    {
        lock (_gate)
        {
            _accepting = false;
            _chunks.Clear();
            _bufferedBytes = 0;
        }
    }

    private byte[] TrimToBudget(byte[] chunk)
    {
        if (chunk.Length <= _maxBufferedBytes)
            return chunk;

        var retained = new byte[_maxBufferedBytes];
        var sourceOffset = chunk.Length - _maxBufferedBytes;
        sourceOffset -= sourceOffset % _blockAlign;
        Buffer.BlockCopy(chunk, sourceOffset, retained, 0, retained.Length);
        return retained;
    }
}
