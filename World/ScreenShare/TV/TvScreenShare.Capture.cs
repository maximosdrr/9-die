using System;
using System.IO;
using System.Runtime.InteropServices;
using Godot;
using NAudio.Wave;

public partial class TvScreenShare
{
    private void StartLocalCapture()
    {
        if (_videoCapture == null)
            _videoCapture = new ScreenCaptureWorker(CaptureWidth, CaptureHeight, TargetFps, _pendingCaptureWindow);

        if (_audioCapture != null)
            return;

        TryStartAudioCapture(static () => new SystemAudioCapture(), GD.PushWarning);
    }

    internal bool TryStartAudioCapture(
        Func<IAudioCaptureSource> captureFactory,
        Action<string> warningSink)
    {
        ArgumentNullException.ThrowIfNull(captureFactory);
        ArgumentNullException.ThrowIfNull(warningSink);
        if (_audioCapture != null)
            return true;

        IAudioCaptureSource capture = null;
        try
        {
            capture = captureFactory();
            if (capture == null)
                throw new InvalidOperationException("a fonte de áudio não foi criada");

            if (capture.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat
                || capture.WaveFormat.BitsPerSample != 32)
            {
                ReportAudioFallback(
                    "formato incompatível; esperado PCM float32",
                    warningSink);
                DisposeFailedAudioCapture(capture);
                return false;
            }

            capture.Start();
            _audioCapture = capture;
            return true;
        }
        catch (Exception exception)
        {
            DisposeFailedAudioCapture(capture);
            var detail = string.IsNullOrWhiteSpace(exception.Message)
                ? exception.GetType().Name
                : exception.Message;
            ReportAudioFallback(detail, warningSink);
            return false;
        }
    }

    private void StopLocalCapture()
    {
        _videoCapture?.Dispose();
        _videoCapture = null;

        StopAudioCapture(GD.PushWarning);
        _pendingAudioPackets.Clear();
        _audioPacketSendBudget = 0.0;
    }

    internal void StopAudioCapture(Action<string> warningSink)
    {
        ArgumentNullException.ThrowIfNull(warningSink);
        var capture = _audioCapture;
        _audioCapture = null;
        if (capture == null)
            return;

        try
        {
            capture.Stop();
        }
        catch (Exception exception)
        {
            ReportAudioFallback($"falha ao encerrar áudio: {exception.Message}", warningSink);
        }
        finally
        {
            try
            {
                capture.Dispose();
            }
            catch (Exception exception)
            {
                ReportAudioFallback($"falha ao liberar áudio: {exception.Message}", warningSink);
            }
        }
    }

    internal bool HasActiveAudioCapture => _audioCapture != null;

    private void ReportAudioFallback(string detail, Action<string> warningSink)
    {
        if (_audioFallbackWarningIssued)
            return;

        _audioFallbackWarningIssued = true;
        warningSink($"[TvScreenShare] Áudio do sistema indisponível ({detail}); "
            + "o compartilhamento continuará somente com vídeo.");
    }

    private static void DisposeFailedAudioCapture(IAudioCaptureSource capture)
    {
        if (capture == null)
            return;

        try
        {
            capture.Dispose();
        }
        catch
        {
            // The primary failure is reported by the caller. Cleanup failures cannot be allowed
            // to abort video capture or replace the useful device/start diagnostic.
        }
    }

    private void ProcessVideoCapture()
    {
        if (_videoCapture == null)
            return;

        if (_videoCapture.SourceLost)
        {
            GD.PushWarning("A janela compartilhada foi fechada ou ficou inacessivel; parando o compartilhamento.");
            RequestStopSharing();
            return;
        }

        if (!_videoCapture.TryDequeueLatestFrame(out var encodedBytes))
            return;

        _capturedFramesInWindow++;

        var nowMs = Time.GetTicksMsec();
        var sendIntervalMs = NetworkFrameIntervalForPayload(encodedBytes.Length);
        if (nowMs - _lastVideoSendMs < sendIntervalMs)
            return;
        _lastVideoSendMs = nowMs;
        _networkFramesInWindow++;
        _videoBytesInWindow += encodedBytes.Length;
        _videoSamplesInWindow++;

        // Queue the frame before doing the local WebP decode/mipmap work. Previously every
        // captured frame was decoded before this cadence check, so expensive local preview work
        // could make peers receive only a handful of frames even while capture itself was fast.
        if (_steamProvider != null)
        {
            BroadcastSteamPacket(SteamP2PVideoChannel, encodedBytes, VideoQueueLimitBytes);
        }
        else if (Multiplayer.IsServer())
        {
            foreach (var peerId in Multiplayer.GetPeers())
                RpcId(peerId, MethodName.SendFrame, encodedBytes);
        }
        else
        {
            RpcId(1, MethodName.SubmitFrame, encodedBytes);
        }

        // The sharer's TV follows the same 20 fps cadence as viewers. Decoding up to 60 local
        // preview frames offered no network benefit and could starve both networking and gameplay.
        DisplayFrame(encodedBytes);
    }

    private void ProcessAudioCapture(double delta)
    {
        if (_audioCapture == null)
            return;

        using var stream = new MemoryStream();
        var any = false;
        while (_audioCapture.TryDequeueChunk(out var chunk))
        {
            stream.Write(chunk, 0, chunk.Length);
            any = true;
        }

        if (any)
        {
            var monoInt16 = ConvertFloatToMonoInt16(
                stream.ToArray(),
                _audioCapture.WaveFormat.Channels,
                _audioCapture.WaveFormat.SampleRate);
            QueueCapturedAudio(monoInt16);
        }

        FlushCapturedAudio(delta);
    }

    private void QueueCapturedAudio(byte[] monoInt16)
    {
        // Fixed 10 ms packets stay below a typical network MTU and fit the receiver's short
        // AudioStreamGenerator buffer. The queue keeps only the latest 120 ms.
        for (var offset = 0; offset < monoInt16.Length; offset += AudioPacketBytes)
        {
            var packetLength = Math.Min(AudioPacketBytes, monoInt16.Length - offset);
            packetLength -= packetLength % sizeof(short);
            if (packetLength <= 0)
                break;

            var packet = new byte[packetLength];
            Buffer.BlockCopy(monoInt16, offset, packet, 0, packetLength);

            while (_pendingAudioPackets.Count >= MaxPendingAudioPackets)
                _pendingAudioPackets.Dequeue();
            _pendingAudioPackets.Enqueue(packet);
        }
    }

    private void FlushCapturedAudio(double delta)
    {
        // WASAPI callbacks arrive in bursts. Sending an entire burst in one _Process caused a
        // periodic main-thread spike and starved video. A small token budget spreads the same
        // live packets across frames without ever allowing an unbounded backlog.
        if (_pendingAudioPackets.Count == 0)
        {
            // Do not bank credits during silence; otherwise the next callback would still flush
            // a full burst in one frame and recreate the hitch this pacing is meant to remove.
            _audioPacketSendBudget = 0.0;
            return;
        }

        _audioPacketSendBudget = Math.Min(
            MaxAudioPacketsPerFrame,
            _audioPacketSendBudget + Math.Max(0.0, delta) * AudioPacketsPerSecond);

        var packetsThisFrame = Math.Min(
            MaxAudioPacketsPerFrame,
            (int)_audioPacketSendBudget);
        while (packetsThisFrame-- > 0 && _pendingAudioPackets.Count > 0)
        {
            SendCapturedAudioPacket(_pendingAudioPackets.Dequeue());
            _audioPacketSendBudget -= 1.0;
        }

        if (_pendingAudioPackets.Count == 0)
            _audioPacketSendBudget = 0.0;
    }

    private void SendCapturedAudioPacket(byte[] monoInt16)
    {
        // Deliberately not calling PlayAudioChunk here: the sharer already hears this audio
        // directly from their own system output, so looping it back through the TV speaker
        // would double it up as an echo. Only relay it to everyone else.
        if (_steamProvider != null)
        {
            // Unreliable-no-delay audio never enters Steam's reliable queue, so querying the
            // aggregate session queue for every 10 ms packet is both meaningless and expensive.
            BroadcastSteamPacket(SteamP2PAudioChannel, monoInt16);
            return;
        }

        _audioPacketsSentInWindow++;
        if (Multiplayer.IsServer())
        {
            foreach (var peerId in Multiplayer.GetPeers())
                RpcId(peerId, MethodName.SendAudioChunk, monoInt16);
        }
        else
        {
            RpcId(1, MethodName.SubmitAudioChunk, monoInt16);
        }
    }

    private static byte[] ConvertFloatToMonoInt16(byte[] floatBytes, int channels, int sourceSampleRate)
    {
        if (channels <= 0)
            return Array.Empty<byte>();

        var frameCount = floatBytes.Length / 4 / channels;
        if (frameCount <= 0)
            return Array.Empty<byte>();

        var floatSamples = MemoryMarshal.Cast<byte, float>(floatBytes);
        var mono = new float[frameCount];

        for (var i = 0; i < frameCount; i++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
                sum += floatSamples[i * channels + c];

            mono[i] = Mathf.Clamp(sum / channels, -1f, 1f);
        }

        // WasapiLoopbackCapture uses whatever mix format the sharer's default output device is
        // currently running (varies per machine/driver — 44.1kHz, 48kHz, 96kHz are all common),
        // while every viewer's AudioStreamGenerator is fixed at AudioMixRate. Without matching
        // the two up here, playback comes out pitched/slowed on any viewer whenever the sharer's
        // device isn't already at AudioMixRate — resampling on the sharer's side keeps every
        // viewer's fixed-rate generator correct regardless of the sharer's hardware.
        if (sourceSampleRate != AudioMixRate)
            mono = ResampleLinear(mono, sourceSampleRate, AudioMixRate);

        var monoInt16 = new byte[mono.Length * 2];

        for (var i = 0; i < mono.Length; i++)
        {
            var int16Sample = (short)(mono[i] * short.MaxValue);

            monoInt16[i * 2] = (byte)(int16Sample & 0xFF);
            monoInt16[i * 2 + 1] = (byte)((int16Sample >> 8) & 0xFF);
        }

        return monoInt16;
    }

    private static float[] ResampleLinear(float[] input, int sourceRate, int targetRate)
    {
        if (sourceRate <= 0 || targetRate <= 0 || input.Length == 0)
            return input;

        var ratio = (double)sourceRate / targetRate;
        var outputLength = (int)(input.Length / ratio);
        if (outputLength <= 0)
            return Array.Empty<float>();

        var output = new float[outputLength];

        for (var i = 0; i < outputLength; i++)
        {
            var srcPos = i * ratio;
            var srcIndex = (int)srcPos;
            var frac = srcPos - srcIndex;

            var a = input[srcIndex];
            var b = srcIndex + 1 < input.Length ? input[srcIndex + 1] : a;

            output[i] = (float)(a + (b - a) * frac);
        }

        return output;
    }

    private static Vector2[] ConvertMonoInt16ToFrames(byte[] monoInt16)
    {
        var sampleCount = monoInt16.Length / 2;
        if (sampleCount <= 0)
            return Array.Empty<Vector2>();

        var samples = MemoryMarshal.Cast<byte, short>(monoInt16);
        var frames = new Vector2[sampleCount];

        for (var i = 0; i < sampleCount; i++)
        {
            var f = samples[i] / (float)short.MaxValue;
            frames[i] = new Vector2(f, f);
        }

        return frames;
    }
}
