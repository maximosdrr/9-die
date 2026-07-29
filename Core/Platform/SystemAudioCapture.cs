using System;
using System.Collections.Concurrent;
using NAudio.Wave;

/// <summary>
/// Captures the system's audio output (WASAPI loopback) into a thread-safe queue of raw PCM
/// byte chunks. NAudio's capture callback fires on its own thread, so consumers must drain
/// TryDequeueChunk from the main/Godot thread instead of touching Godot APIs from the callback.
/// </summary>
public sealed class SystemAudioCapture : IDisposable
{
    private readonly WasapiLoopbackCapture _capture;
    private readonly ConcurrentQueue<byte[]> _pcmChunks = new();

    public WaveFormat WaveFormat => _capture.WaveFormat;

    public SystemAudioCapture()
    {
        _capture = new WasapiLoopbackCapture();
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
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Dispose();
    }
}
