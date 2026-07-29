using Godot;
using System;
using System.Collections.Concurrent;
using System.Threading;

/// <summary>
/// Captures and WebP-encodes the primary screen on a dedicated background thread, at a fixed
/// pace, so this work never blocks Godot's main thread / render loop. Capture (pure Win32 GDI)
/// and Godot's Image encode/decode methods don't touch the RenderingServer or scene tree, so
/// they're safe to run off-thread; only the final byte[] needs to cross back to the main thread
/// (via TryDequeueLatestFrame, called from _Process) before touching a Texture/Node/RPC.
/// </summary>
public sealed class ScreenCaptureWorker : IDisposable
{
    private readonly Thread _thread;
    private readonly ConcurrentQueue<byte[]> _frames = new();
    private volatile bool _running;

    private readonly int _width;
    private readonly int _height;
    private readonly int _frameIntervalMs;

    public ScreenCaptureWorker(int width, int height, double targetFps)
    {
        _width = width;
        _height = height;
        _frameIntervalMs = Math.Max(1, (int)(1000.0 / targetFps));

        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "TvScreenCapture" };
        _thread.Start();
    }

    private void Run()
    {
        while (_running)
        {
            var start = System.Environment.TickCount;

            try
            {
                if (WindowsScreenCapture.TryCapturePrimaryScreen(_width, _height, out var rgba))
                {
                    var image = Image.CreateFromData(_width, _height, false, Image.Format.Rgba8, rgba);
                    // Lossless: lossy WebP always chroma-subsamples (4:2:0), which smears color
                    // at sharp text/UI edges. Desktop content is exactly the case lossless WebP
                    // compresses well (flat colors, repeated patterns), and bandwidth is not a
                    // constraint here.
                    var encoded = image.SaveWebpToBuffer(false);
                    _frames.Enqueue(encoded);
                }
            }
            catch
            {
                // A single dropped frame is harmless; the next iteration just tries again.
            }

            var elapsed = System.Environment.TickCount - start;
            var sleepMs = _frameIntervalMs - elapsed;
            if (sleepMs > 0)
                Thread.Sleep(sleepMs);
        }
    }

    /// <summary>
    /// Drains the queue and returns only the most recently captured frame, discarding any
    /// older ones that piled up if the main thread fell behind for a moment — we always want
    /// to display/send the freshest frame, never a growing backlog of stale ones.
    /// </summary>
    public bool TryDequeueLatestFrame(out byte[] frame)
    {
        frame = null;
        var got = false;

        while (_frames.TryDequeue(out var next))
        {
            frame = next;
            got = true;
        }

        return got;
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join(500);
    }
}
