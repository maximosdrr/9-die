using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using NAudio.Wave;

/// <summary>
/// Guards the media boundary independently from capture hardware and Steam. These checks make the
/// media boundary executable: incoming allocations are bounded, media uses dedicated channels,
/// and only connected peers can reserve the TV.
/// </summary>
public partial class TvScreenShareSecurityTest : Node
{
    private int _passed;
    private int _failed;

    public override async void _Ready()
    {
        GD.Print("=== Teste de segurança do compartilhamento de tela ===");

        TestPayloadLimits();
        TestWebpHeaderPreflight();
        TestNativeImportPolicy();
        TestCaptureBufferBounds();
        TestMediaRpcDeliveryMode();
        TestPlayoutQueueIsBounded();
        TestCaptureProducerQueuesAreBounded();
        TestAudioCaptureFallback();
        TestSteamDrainBudget();
        TestCaptureRateDoesNotWasteEncoderWork();
        TestSharingAuthorization();
        await TestTvSceneLifecycle();

        GD.Print($"=== {_passed} passaram, {_failed} falharam ===");
        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    private async System.Threading.Tasks.Task TestTvSceneLifecycle()
    {
        var scene = GD.Load<PackedScene>("res://World/ScreenShare/TV/Tv.tscn");
        var tv = scene?.Instantiate<TvScreenShare>();
        Check("cena da TV pode ser instanciada", tv != null);
        if (tv == null)
            return;

        AddChild(tv);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var playAudio = typeof(TvScreenShare).GetMethod(
            "PlayAudioChunk", BindingFlags.Instance | BindingFlags.NonPublic);
        playAudio?.Invoke(tv, [new byte[960]]);
        Check("playback de áudio pode ser iniciado", playAudio != null);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        tv.QueueFree();
        // The audio server releases a stopped playback on its own mixing thread. Give that
        // thread enough frames to drain before the test process performs leak diagnostics.
        for (var frame = 0; frame < 30; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Check("cena da TV libera captura e playback", !GodotObject.IsInstanceValid(tv));
    }

    private void TestPayloadLimits()
    {
        Check("frame vazio é rejeitado", !TvScreenShare.IsValidMediaPayload([], isAudio: false));
        Check("frame no limite é aceito",
            TvScreenShare.IsValidMediaPayload(new byte[512 * 1024], isAudio: false));
        Check("frame acima de 512 KiB é rejeitado",
            !TvScreenShare.IsValidMediaPayload(new byte[512 * 1024 + 1], isAudio: false));

        Check("pacote de áudio PCM16 de 10 ms é aceito",
            TvScreenShare.IsValidMediaPayload(new byte[48_000 * sizeof(short) / 100], isAudio: true));
        Check("áudio com amostra truncada é rejeitado",
            !TvScreenShare.IsValidMediaPayload(new byte[3], isAudio: true));
        Check("áudio acima de 10 ms é rejeitado para não acumular atraso",
            !TvScreenShare.IsValidMediaPayload(new byte[48_000 * sizeof(short) / 100 + 2], isAudio: true));
    }

    private void TestWebpHeaderPreflight()
    {
        using var image = Image.CreateEmpty(1280, 720, false, Image.Format.Rgba8);
        var godotEncodedFrame = image.SaveWebpToBuffer(lossy: true, quality: 0.9f);
        Check("preflight aceita um frame produzido pelo encoder do Godot",
            TvScreenShare.IsSafeEncodedFrame(godotEncodedFrame));

        var expectedFrame = BuildVp8Header(1280, 720);
        Check("preflight aceita WebP VP8 com o canvas esperado",
            TvScreenShare.IsSafeEncodedFrame(expectedFrame));

        var oversizedCanvas = BuildVp8Header(16_383, 16_383);
        Check("preflight rejeita bomba de descompressao WebP antes do codec",
            !TvScreenShare.IsSafeEncodedFrame(oversizedCanvas));

        var truncated = expectedFrame[..^1];
        Check("preflight rejeita WebP truncado ou com tamanho RIFF inconsistente",
            !TvScreenShare.IsSafeEncodedFrame(truncated));

        var malformed = (byte[])expectedFrame.Clone();
        malformed[23] = 0;
        Check("preflight rejeita assinatura VP8 invalida",
            !TvScreenShare.IsSafeEncodedFrame(malformed));
    }

    private void TestNativeImportPolicy()
    {
        var policy = typeof(WindowsScreenCapture).Assembly
            .GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();
        Check("imports nativos de captura sao restritos ao System32",
            policy?.Paths == DllImportSearchPath.System32);
    }

    private void TestCaptureBufferBounds()
    {
        Check("captura aceita buffer com tamanho exato",
            WindowsScreenCapture.HasValidDestination(1, 1, new byte[4]));
        Check("captura rejeita dimensoes cujo calculo excederia Int64",
            !WindowsScreenCapture.HasValidDestination(
                int.MaxValue, int.MaxValue, new byte[4]));
    }

    private static byte[] BuildVp8Header(int width, int height)
    {
        var frame = new byte[30];
        WriteFourCc(frame, 0, "RIFF");
        WriteUInt32(frame, 4, (uint)(frame.Length - 8));
        WriteFourCc(frame, 8, "WEBP");
        WriteFourCc(frame, 12, "VP8 ");
        WriteUInt32(frame, 16, 10);

        // Minimal key-frame header. The preflight deliberately does not attempt full decoding.
        frame[20] = 0;
        frame[23] = 0x9d;
        frame[24] = 0x01;
        frame[25] = 0x2a;
        frame[26] = (byte)width;
        frame[27] = (byte)(width >> 8);
        frame[28] = (byte)height;
        frame[29] = (byte)(height >> 8);
        return frame;
    }

    private static void WriteFourCc(byte[] target, int offset, string value)
    {
        for (var index = 0; index < 4; index++)
            target[offset + index] = (byte)value[index];
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
        target[offset + 3] = (byte)(value >> 24);
    }

    private void TestMediaRpcDeliveryMode()
    {
        CheckRpcUsesReliableMediaDelivery("SubmitFrame", expectedChannel: 1);
        CheckRpcUsesReliableMediaDelivery("SendFrame", expectedChannel: 1);
        CheckRpcUsesDisposableAudioDelivery("SubmitAudioChunk", expectedChannel: 2);
        CheckRpcUsesDisposableAudioDelivery("SendAudioChunk", expectedChannel: 2);
    }

    private void CheckRpcUsesReliableMediaDelivery(string methodName, int expectedChannel)
    {
        var method = typeof(TvScreenShare).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        var rpc = method?.GetCustomAttribute<RpcAttribute>();

        Check($"{methodName} usa reliable no canal dedicado {expectedChannel}",
            rpc != null
            && rpc.TransferMode == MultiplayerPeer.TransferModeEnum.Reliable
            && rpc.TransferChannel == expectedChannel);
    }

    private void CheckRpcUsesDisposableAudioDelivery(string methodName, int expectedChannel)
    {
        var method = typeof(TvScreenShare).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        var rpc = method?.GetCustomAttribute<RpcAttribute>();

        Check($"{methodName} usa unreliable ordered no canal de áudio {expectedChannel}",
            rpc != null
            && rpc.TransferMode == MultiplayerPeer.TransferModeEnum.UnreliableOrdered
            && rpc.TransferChannel == expectedChannel);
    }

    private void TestPlayoutQueueIsBounded()
    {
        var buffer = new VideoPlayoutBuffer();
        for (var i = 0; i < 100; i++)
            buffer.Enqueue([(byte)i]);

        Check("fila de vídeo mantém no máximo quatro frames", buffer.BufferedFrameCount == 4);
        buffer.Clear();
        Check("fila de vídeo pode ser liberada no lifecycle", buffer.BufferedFrameCount == 0);
    }

    private void TestSteamDrainBudget()
    {
        Check("leitura Steam tem orçamento rígido de pacotes por frame",
            TvScreenShare.SteamDrainPacketBudget is > 0 and <= 32);
        Check("leitura Steam tem orçamento rígido de bytes por frame",
            TvScreenShare.SteamDrainByteBudget is >= 512 * 1024 and <= 2 * 1024 * 1024);
        Check("fila Steam mantém no máximo um frame completo",
            TvScreenShare.SteamVideoQueueLimitForPayload(160 * 1024) == 160 * 1024
            && TvScreenShare.SteamVideoQueueLimitForPayload(1) == 128 * 1024
            && TvScreenShare.SteamVideoQueueLimitForPayload(4 * 1024 * 1024)
                == 512 * 1024);
    }

    private void TestCaptureRateDoesNotWasteEncoderWork()
    {
        Check("captura mantém apenas a margem necessária sobre os 20 fps de rede",
            TvScreenShare.NetworkTargetFps == 20.0
            && TvScreenShare.CaptureTargetFps >= TvScreenShare.NetworkTargetFps
            && TvScreenShare.CaptureTargetFps <= TvScreenShare.NetworkTargetFps * 1.5);
        Check("cadência de vídeo se adapta ao peso do frame sem exceder o teto",
            TvScreenShare.NetworkFrameIntervalForPayload(20 * 1024) == 50
            && TvScreenShare.NetworkFrameIntervalForPayload(160 * 1024) == 157
            && TvScreenShare.NetworkFrameIntervalForPayload(512 * 1024) == 500);
    }

    private void TestCaptureProducerQueuesAreBounded()
    {
        var videoSlot = new ScreenCaptureWorker.LatestFrameSlot();
        for (var sequence = 0; sequence < 10_000; sequence++)
            videoSlot.Publish(sequence, [(byte)(sequence % 256)]);

        Check("produtor de vídeo sem consumidor mantém somente um frame", videoSlot.Count == 1);
        var tookLatest = videoSlot.TryTake(out var latestSequence, out _);
        Check("slot de vídeo entrega o frame mais recente",
            tookLatest && latestSequence == 9_999 && videoSlot.Count == 0);

        videoSlot.Publish(20, [20]);
        videoSlot.Publish(19, [19]);
        videoSlot.TryTake(out var outOfOrderSequence, out _);
        Check("encoder atrasado não substitui frame mais novo", outOfOrderSequence == 20);

        videoSlot.Publish(21, [21]);
        videoSlot.CompleteAndClear();
        videoSlot.Publish(22, [22]);
        Check("dispose limpa e fecha o slot de vídeo", videoSlot.Count == 0);

        var audioQueue = new BoundedAudioChunkQueue(maxBufferedBytes: 8, blockAlign: 2);
        audioQueue.Enqueue([1, 1, 1, 1]);
        audioQueue.Enqueue([2, 2, 2, 2]);
        audioQueue.Enqueue([3, 3, 3, 3]);

        Check("produtor de áudio descarta o chunk mais antigo",
            audioQueue.BufferedBytes == 8
            && audioQueue.ChunkCount == 2
            && audioQueue.TryDequeue(out var oldestRetained)
            && oldestRetained[0] == 2);

        for (var chunk = 0; chunk < 10_000; chunk++)
            audioQueue.Enqueue(new byte[4]);

        Check("produtor de áudio sem consumidor respeita teto rígido",
            audioQueue.BufferedBytes <= 8 && audioQueue.ChunkCount <= 2);

        var oversizedQueue = new BoundedAudioChunkQueue(maxBufferedBytes: 10, blockAlign: 2);
        var oversizedChunk = new byte[14];
        for (var index = 0; index < oversizedChunk.Length; index++)
            oversizedChunk[index] = (byte)index;
        oversizedQueue.Enqueue(oversizedChunk);
        oversizedQueue.TryDequeue(out var trimmedChunk);
        Check("chunk de áudio maior que o orçamento preserva somente a cauda",
            trimmedChunk.Length == 10 && trimmedChunk[0] == 4 && trimmedChunk[^1] == 13);

        audioQueue.CompleteAndClear();
        Check("dispose limpa e fecha a fila de áudio",
            audioQueue.BufferedBytes == 0
            && audioQueue.ChunkCount == 0
            && !audioQueue.Enqueue([4, 4]));
    }

    private void TestSharingAuthorization()
    {
        Check("host pode reservar a TV",
            TvScreenShare.IsKnownSessionPeer(1, localPeerId: 1, connectedPeerIds: [7]));
        Check("peer conectado pode reservar a TV sem depender da colisão remota",
            TvScreenShare.IsKnownSessionPeer(7, localPeerId: 1, connectedPeerIds: [7]));
        Check("peer desconhecido não pode reservar a TV",
            !TvScreenShare.IsKnownSessionPeer(8, localPeerId: 1, connectedPeerIds: [7]));

        var tv = new TvScreenShare();
        var acceptedRequests = true;
        for (var request = 0;
            request < TvScreenShare.ShareControlRequestsPerSecond;
            request++)
        {
            acceptedRequests &= tv.TryConsumeShareControlRequest(7, 700);
        }

        Check("start e stop da TV compartilham orçamento por peer",
            acceptedRequests && !tv.TryConsumeShareControlRequest(7, 700));

        tv.Free();
    }

    private void TestAudioCaptureFallback()
    {
        var tv = new TvScreenShare();
        var warnings = new System.Collections.Generic.List<string>();
        var startFailure = new FakeAudioCapture { ThrowOnStart = true };

        var started = tv.TryStartAudioCapture(() => startFailure, warnings.Add);
        Check("falha ao iniciar WASAPI degrada para vídeo-only",
            !started && !tv.HasActiveAudioCapture);
        Check("captura parcialmente criada é liberada após falha no Start",
            startFailure.StartCalled && startFailure.Disposed);

        var creationResult = tv.TryStartAudioCapture(
            () => throw new InvalidOperationException("sem dispositivo padrão"),
            warnings.Add);
        Check("falha ao criar WASAPI também preserva o compartilhamento",
            !creationResult && !tv.HasActiveAudioCapture);
        Check("fallback de áudio emite um único aviso útil",
            warnings.Count == 1 && warnings[0].Contains("somente com vídeo"));

        var stopFailure = new FakeAudioCapture { ThrowOnStop = true };
        Check("captura de áudio pode se recuperar em tentativa posterior",
            tv.TryStartAudioCapture(() => stopFailure, warnings.Add)
            && tv.HasActiveAudioCapture);
        tv.StopAudioCapture(warnings.Add);
        Check("Dispose ocorre mesmo quando Stop lança",
            stopFailure.StopCalled && stopFailure.Disposed && !tv.HasActiveAudioCapture);
        Check("falha de cleanup não repete o aviso de fallback", warnings.Count == 1);

        tv.Free();
    }

    private sealed class FakeAudioCapture : IAudioCaptureSource
    {
        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);

        public bool ThrowOnStart { get; init; }
        public bool ThrowOnStop { get; init; }
        public bool StartCalled { get; private set; }
        public bool StopCalled { get; private set; }
        public bool Disposed { get; private set; }

        public void Start()
        {
            StartCalled = true;
            if (ThrowOnStart)
                throw new InvalidOperationException("falha simulada no StartRecording");
        }

        public void Stop()
        {
            StopCalled = true;
            if (ThrowOnStop)
                throw new InvalidOperationException("falha simulada no StopRecording");
        }

        public bool TryDequeueChunk(out byte[] chunk)
        {
            chunk = null;
            return false;
        }

        public void Dispose() => Disposed = true;
    }

    private void Check(string label, bool condition)
    {
        if (condition)
        {
            _passed++;
            GD.Print($"  OK   {label}");
        }
        else
        {
            _failed++;
            GD.Print($"  FALHA {label}");
        }
    }
}
