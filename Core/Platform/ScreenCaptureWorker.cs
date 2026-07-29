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
    private volatile bool _sourceLost;

    // Lossy at high quality: lossless WebP's compressor does a much heavier multi-pass search
    // (backward references, color cache) than lossy's single-pass transform, which was the real
    // bottleneck capping the achieved frame rate far below TargetFps regardless of CPU power —
    // the earlier "cartoon" quality complaints turned out to be caused by the GDI stretch mode
    // and material bloom, both already fixed independently, so this should still look sharp.
    private const float WebpQuality = 0.9f;

    private readonly int _width;
    private readonly int _height;
    private readonly int _frameIntervalMs;
    private readonly IntPtr _windowHandle;
    private readonly int _failureThreshold;

    public bool SourceLost => _sourceLost;

    public ScreenCaptureWorker(int width, int height, double targetFps, IntPtr windowHandle = default)
    {
        _width = width;
        _height = height;
        _frameIntervalMs = Math.Max(1, (int)(1000.0 / targetFps));
        _windowHandle = windowHandle;
        // ~2 seconds of consecutive failed captures — long enough to ignore a single missed
        // frame, short enough that a closed/gone window is noticed quickly.
        _failureThreshold = Math.Max(1, 2000 / _frameIntervalMs);

        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "TvScreenCapture" };
        _thread.Start();
    }

    private void Run()
    {
        var consecutiveFailures = 0;
        var framesSinceReport = 0;
        var reportWindowStart = System.Environment.TickCount;

        while (_running)
        {
            var start = System.Environment.TickCount;

            try
            {
                var captured = _windowHandle == IntPtr.Zero
                    ? WindowsScreenCapture.TryCapturePrimaryScreen(_width, _height, out var rgba)
                    : WindowsScreenCapture.TryCaptureWindow(_windowHandle, _width, _height, out rgba);

                if (captured)
                {
                    consecutiveFailures = 0;

                    var image = Image.CreateFromData(_width, _height, false, Image.Format.Rgba8, rgba);
                    var encoded = image.SaveWebpToBuffer(true, WebpQuality);
                    _frames.Enqueue(encoded);

                    framesSinceReport++;
                }
                else if (_windowHandle != IntPtr.Zero)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= _failureThreshold)
                        _sourceLost = true;
                }
            }
            catch
            {
                // A single dropped frame is harmless; the next iteration just tries again.
            }

            var now = System.Environment.TickCount;

            // Cheap, always-on diagnostic: makes the *actual* sustained capture+encode rate
            // visible in the output console, instead of guessing from how the video "feels".
            if (now - reportWindowStart >= 1000)
            {
                var elapsedSeconds = (now - reportWindowStart) / 1000.0;
                var achievedFps = framesSinceReport / elapsedSeconds;
                var targetFps = 1000.0 / _frameIntervalMs;
                GD.Print($"[TvScreenCapture] {achievedFps:F1} fps (alvo: {targetFps:F1})");
                framesSinceReport = 0;
                reportWindowStart = now;
            }

            var elapsed = now - start;
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
