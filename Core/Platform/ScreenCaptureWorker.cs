using Godot;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

/// <summary>
/// Captures and WebP-encodes the primary screen (or one window) off the main thread. Capture
/// (pure Win32 GDI) and Godot's Image encode methods don't touch the RenderingServer or scene
/// tree, so they're safe off-thread; only the final byte[] crosses back to the main thread
/// (via TryDequeueLatestFrame, called from _Process) before touching a Texture/Node/RPC.
///
/// The pipeline is split into one capture thread plus a small pool of encoder threads. A single
/// thread doing capture (~26 ms) then encode (~70 ms) serially caps out near 10 fps no matter
/// how fast each half gets. Encoding one WebP frame is single-threaded inside libwebp, but
/// different frames are independent, so N encoders running side by side multiply throughput
/// without touching the codec. Frames carry a sequence number; the consumer only ever wants the
/// freshest frame, so out-of-order completion is resolved by discarding anything older than the
/// newest already delivered.
/// </summary>
public sealed class ScreenCaptureWorker : IDisposable
{
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

    private readonly Thread _captureThread;
    private readonly Thread[] _encoderThreads;

    // Pixel buffers cycle free pool -> filled by capture -> encode queue -> back to the pool.
    // A fixed set allocated once, because at 1280x720 each buffer is 3.5 MB — far past the 85 KB
    // threshold that sends an allocation to the Large Object Heap, so fresh per-frame buffers
    // made the GC, not the capture, the thing setting the pace. The pool doubles as backpressure:
    // when every buffer is in flight, the capture thread waits instead of racing ahead of the
    // encoders and piling up frames nobody will ever display.
    private readonly BlockingCollection<byte[]> _freeBuffers;
    private readonly BlockingCollection<(long Seq, byte[] Pixels)> _encodeQueue;
    private readonly ConcurrentQueue<(long Seq, byte[] Encoded)> _readyFrames;

    private volatile bool _running;
    private volatile bool _sourceLost;
    private long _lastDeliveredSeq = -1;

    // Encoder-side diagnostics, written by several threads at once — hence Interlocked.
    private long _encodeTicksAccum;
    private long _framesEncodedAccum;

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

        // Enough encoders that the (faster) capture stage rarely waits on a busy pool — encode
        // is ~2.7x the cost of capture, so three keep up with one — but capped so the pool can't
        // starve the game's own main/render threads on smaller CPUs.
        var encoderCount = Math.Clamp(System.Environment.ProcessorCount - 4, 2, 3);

        _freeBuffers = new BlockingCollection<byte[]>();
        for (var i = 0; i < encoderCount + 2; i++)
            _freeBuffers.Add(new byte[width * height * 4]);

        _encodeQueue = new BlockingCollection<(long, byte[])>();
        _readyFrames = new ConcurrentQueue<(long, byte[])>();

        _running = true;

        _encoderThreads = new Thread[encoderCount];
        for (var i = 0; i < encoderCount; i++)
        {
            _encoderThreads[i] = new Thread(RunEncoder) { IsBackground = true, Name = $"TvFrameEncoder{i}" };
            _encoderThreads[i].Start();
        }

        _captureThread = new Thread(RunCapture) { IsBackground = true, Name = "TvScreenCapture" };
        _captureThread.Start();
    }

    // Stopwatch rather than Environment.TickCount throughout: the latter only advances every
    // ~15.6 ms, which is coarser than the frame interval we're trying to hold, so it can neither
    // pace the loop nor measure a stage accurately.
    private static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private void RunCapture()
    {
        var seq = 0L;
        var consecutiveFailures = 0;
        var capturesSinceReport = 0;
        var captureTicks = 0L;
        var reportWindowStart = Stopwatch.GetTimestamp();

        while (_running)
        {
            var start = Stopwatch.GetTimestamp();

            // Short timeout instead of an unbounded Take so the loop keeps checking _running.
            if (!_freeBuffers.TryTake(out var buffer, 250))
                continue;

            var captured = false;
            try
            {
                captured = _windowHandle == IntPtr.Zero
                    ? WindowsScreenCapture.TryCapturePrimaryScreen(_width, _height, buffer)
                    : WindowsScreenCapture.TryCaptureWindow(_windowHandle, _width, _height, buffer);
            }
            catch
            {
                // A single dropped frame is harmless; the next iteration just tries again.
            }

            captureTicks += Stopwatch.GetTimestamp() - start;
            capturesSinceReport++;

            if (captured)
            {
                consecutiveFailures = 0;
                _encodeQueue.Add((seq++, buffer));
            }
            else
            {
                _freeBuffers.Add(buffer);
                if (_windowHandle != IntPtr.Zero && ++consecutiveFailures >= _failureThreshold)
                    _sourceLost = true;
            }

            var now = Stopwatch.GetTimestamp();

            // Cheap, always-on diagnostic: makes the *actual* sustained rate visible in the
            // output console, instead of guessing from how the video "feels". The two per-stage
            // averages say which half of the pipeline to attack when it falls short of target.
            var windowMs = ToMilliseconds(now - reportWindowStart);
            if (windowMs >= 1000)
            {
                var framesEncoded = Interlocked.Exchange(ref _framesEncodedAccum, 0);
                var encodeTicks = Interlocked.Exchange(ref _encodeTicksAccum, 0);

                var achievedFps = framesEncoded / (windowMs / 1000.0);
                var targetFps = 1000.0 / _frameIntervalMs;
                var captureMs = capturesSinceReport > 0 ? ToMilliseconds(captureTicks) / capturesSinceReport : 0.0;
                var encodeMs = framesEncoded > 0 ? ToMilliseconds(encodeTicks) / framesEncoded : 0.0;

                GD.Print($"[TvScreenCapture] {achievedFps:F1} fps (alvo: {targetFps:F1}) | captura {captureMs:F1} ms | encode {encodeMs:F1} ms x{_encoderThreads.Length}");

                capturesSinceReport = 0;
                captureTicks = 0;
                reportWindowStart = now;
            }

            var sleepMs = _frameIntervalMs - (int)ToMilliseconds(now - start);
            if (sleepMs > 0)
                Thread.Sleep(sleepMs);
        }
    }

    private void RunEncoder()
    {
        // GetConsumingEnumerable blocks while the queue is empty and ends when Dispose calls
        // CompleteAdding, so this doubles as the thread's lifetime.
        foreach (var (frameSeq, pixels) in _encodeQueue.GetConsumingEnumerable())
        {
            try
            {
                var start = Stopwatch.GetTimestamp();
                var image = Image.CreateFromData(_width, _height, false, Image.Format.Rgba8, pixels);
                var encoded = image.SaveWebpToBuffer(true, WebpQuality);

                Interlocked.Add(ref _encodeTicksAccum, Stopwatch.GetTimestamp() - start);
                Interlocked.Increment(ref _framesEncodedAccum);

                _readyFrames.Enqueue((frameSeq, encoded));
            }
            catch
            {
                // Dropping one frame is harmless.
            }
            finally
            {
                _freeBuffers.Add(pixels);
            }
        }
    }

    /// <summary>
    /// Drains the queue and returns only the most recently captured frame, discarding any older
    /// ones — whether they piled up because the main thread fell behind, or because parallel
    /// encoders finished out of order. We always want the freshest frame, never a step backward.
    /// </summary>
    public bool TryDequeueLatestFrame(out byte[] frame)
    {
        frame = null;
        var newest = long.MinValue;

        while (_readyFrames.TryDequeue(out var item))
        {
            if (item.Seq > newest)
            {
                newest = item.Seq;
                frame = item.Encoded;
            }
        }

        if (frame == null || newest <= _lastDeliveredSeq)
        {
            frame = null;
            return false;
        }

        _lastDeliveredSeq = newest;
        return true;
    }

    public void Dispose()
    {
        // Order matters: the capture thread is the only producer into _encodeQueue, so it must
        // be fully stopped before CompleteAdding — Add after CompleteAdding throws.
        _running = false;
        _captureThread.Join(500);

        _encodeQueue.CompleteAdding();
        foreach (var encoder in _encoderThreads)
            encoder.Join(500);
    }
}
