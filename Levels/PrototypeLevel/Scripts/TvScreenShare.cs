using Godot;
using NAudio.Wave;
using System;
using System.IO;
using System.Runtime.InteropServices;

[GlobalClass]
public partial class TvScreenShare : MeshInstance3D
{
    private const int CaptureWidth = 384;
    private const int CaptureHeight = 216;
    private const float JpegQuality = 0.5f;
    private const double TargetFps = 8.0;
    private const int FrameTransferChannel = 1;
    private const int AudioTransferChannel = 2;
    private const int AudioMixRate = 48000;

    [Signal]
    public delegate void SharerChangedEventHandler(int sharerId);

    public int SharerId { get; private set; } = 0;

    private MeshInstance3D _screenMeshInstance;
    private StandardMaterial3D _screenMaterial;
    private ImageTexture _texture;
    private double _captureAccumulator = 0.0;

    private AudioStreamPlayer3D _audioPlayer;
    private AudioStreamGeneratorPlayback _audioPlayback;
    private SystemAudioCapture _audioCapture;

    public override void _EnterTree()
    {
        Global.Instance.TvScreen = this;
    }

    public override void _ExitTree()
    {
        if (Global.Instance.TvScreen == this)
            Global.Instance.TvScreen = null;

        StopLocalCapture();
    }

    public override void _Ready()
    {
        _screenMeshInstance = GetNode<MeshInstance3D>("Screen");
        _screenMaterial = (StandardMaterial3D)_screenMeshInstance.MaterialOverride;

        _audioPlayer = GetNode<AudioStreamPlayer3D>("Audio");
        var generator = new AudioStreamGenerator
        {
            MixRate = AudioMixRate,
            BufferLength = 0.5f,
        };
        _audioPlayer.Stream = generator;
        _audioPlayer.Play();
        _audioPlayback = (AudioStreamGeneratorPlayback)_audioPlayer.GetStreamPlayback();

        SetProcess(false);

        NetworkManager.Instance.NetworkProvider.PlayerConnected += OnPlayerConnected;
        NetworkManager.Instance.NetworkProvider.PlayerDisconnected += OnPlayerDisconnected;
    }

    public void RequestStartSharing()
    {
        if (Multiplayer.IsServer())
            TryStartShare(Multiplayer.GetUniqueId());
        else
            RpcId(1, MethodName.RequestStartShare);
    }

    public void RequestStopSharing()
    {
        if (Multiplayer.IsServer())
            TryStopShare(Multiplayer.GetUniqueId());
        else
            RpcId(1, MethodName.RequestStopShare);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestStartShare()
    {
        if (!Multiplayer.IsServer())
            return;
        TryStartShare(Multiplayer.GetRemoteSenderId());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestStopShare()
    {
        if (!Multiplayer.IsServer())
            return;
        TryStopShare(Multiplayer.GetRemoteSenderId());
    }

    private void TryStartShare(int requesterId)
    {
        if (SharerId != 0)
            return;

        ApplySharerChange(requesterId);
        BroadcastSharerChange(requesterId);
    }

    private void TryStopShare(int requesterId)
    {
        if (SharerId != requesterId)
            return;

        ApplySharerChange(0);
        BroadcastSharerChange(0);
    }

    private void BroadcastSharerChange(int sharerId)
    {
        foreach (var peerId in Multiplayer.GetPeers())
            RpcId(peerId, MethodName.AnnounceSharer, sharerId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void AnnounceSharer(int sharerId)
    {
        ApplySharerChange(sharerId);
    }

    private void ApplySharerChange(int sharerId)
    {
        SharerId = sharerId;

        if (sharerId == 0)
        {
            ClearScreen();
            _audioPlayback?.ClearBuffer();
        }

        var isLocalSharer = sharerId != 0 && sharerId == Multiplayer.GetUniqueId();
        SetProcess(isLocalSharer);

        if (isLocalSharer)
            StartLocalCapture();
        else
            StopLocalCapture();

        EmitSignal(SignalName.SharerChanged, sharerId);
    }

    private void OnPlayerConnected(int peerId)
    {
        if (!Multiplayer.IsServer())
            return;

        if (SharerId != 0)
            RpcId(peerId, MethodName.AnnounceSharer, SharerId);
    }

    private void OnPlayerDisconnected(int peerId)
    {
        if (!Multiplayer.IsServer())
            return;

        if (peerId == SharerId)
        {
            ApplySharerChange(0);
            BroadcastSharerChange(0);
        }
    }

    private void StartLocalCapture()
    {
        if (_audioCapture != null)
            return;

        var capture = new SystemAudioCapture();

        if (capture.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat || capture.WaveFormat.BitsPerSample != 32)
        {
            GD.PushWarning("Formato de audio do sistema nao suportado para compartilhamento (esperado float32); apenas a tela sera transmitida.");
            capture.Dispose();
            return;
        }

        _audioCapture = capture;
        _audioCapture.Start();
    }

    private void StopLocalCapture()
    {
        if (_audioCapture == null)
            return;

        _audioCapture.Stop();
        _audioCapture.Dispose();
        _audioCapture = null;
    }

    public override void _Process(double delta)
    {
        ProcessVideoCapture(delta);
        ProcessAudioCapture();
    }

    private void ProcessVideoCapture(double delta)
    {
        _captureAccumulator += delta;
        if (_captureAccumulator < 1.0 / TargetFps)
            return;
        _captureAccumulator = 0.0;

        if (!WindowsScreenCapture.TryCapturePrimaryScreen(CaptureWidth, CaptureHeight, out var rgba))
            return;

        var image = Image.CreateFromData(CaptureWidth, CaptureHeight, false, Image.Format.Rgba8, rgba);
        var jpegBytes = image.SaveJpgToBuffer(JpegQuality);

        DisplayFrame(jpegBytes);

        if (Multiplayer.IsServer())
        {
            foreach (var peerId in Multiplayer.GetPeers())
                RpcId(peerId, MethodName.SendFrame, jpegBytes);
        }
        else
        {
            RpcId(1, MethodName.SubmitFrame, jpegBytes);
        }
    }

    private void ProcessAudioCapture()
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

        if (!any)
            return;

        var monoInt16 = ConvertFloatToMonoInt16(stream.ToArray(), _audioCapture.WaveFormat.Channels);
        if (monoInt16.Length == 0)
            return;

        PlayAudioChunk(monoInt16);

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

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = FrameTransferChannel)]
    private void SubmitFrame(byte[] jpegBytes)
    {
        if (!Multiplayer.IsServer())
            return;

        var senderId = Multiplayer.GetRemoteSenderId();
        if (senderId != SharerId)
            return;

        DisplayFrame(jpegBytes);

        foreach (var peerId in Multiplayer.GetPeers())
            if (peerId != senderId)
                RpcId(peerId, MethodName.SendFrame, jpegBytes);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = FrameTransferChannel)]
    private void SendFrame(byte[] jpegBytes)
    {
        DisplayFrame(jpegBytes);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = AudioTransferChannel)]
    private void SubmitAudioChunk(byte[] monoInt16)
    {
        if (!Multiplayer.IsServer())
            return;

        var senderId = Multiplayer.GetRemoteSenderId();
        if (senderId != SharerId)
            return;

        PlayAudioChunk(monoInt16);

        foreach (var peerId in Multiplayer.GetPeers())
            if (peerId != senderId)
                RpcId(peerId, MethodName.SendAudioChunk, monoInt16);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = AudioTransferChannel)]
    private void SendAudioChunk(byte[] monoInt16)
    {
        PlayAudioChunk(monoInt16);
    }

    private void DisplayFrame(byte[] jpegBytes)
    {
        var image = new Image();
        var err = image.LoadJpgFromBuffer(jpegBytes);
        if (err != Error.Ok)
            return;

        if (_texture == null)
        {
            _texture = ImageTexture.CreateFromImage(image);
            _screenMaterial.AlbedoColor = Colors.White;
            _screenMaterial.AlbedoTexture = _texture;
            _screenMaterial.EmissionEnabled = true;
            _screenMaterial.EmissionTexture = _texture;
        }
        else
        {
            _texture.Update(image);
        }
    }

    private void ClearScreen()
    {
        _texture = null;
        _screenMaterial.AlbedoColor = new Color(0.05f, 0.05f, 0.05f);
        _screenMaterial.AlbedoTexture = null;
        _screenMaterial.EmissionEnabled = false;
        _screenMaterial.EmissionTexture = null;
    }

    private void PlayAudioChunk(byte[] monoInt16)
    {
        if (_audioPlayback == null)
            return;

        var frames = ConvertMonoInt16ToFrames(monoInt16);
        if (frames.Length == 0)
            return;

        if (_audioPlayback.CanPushBuffer(frames.Length))
            _audioPlayback.PushBuffer(frames);
    }

    private static byte[] ConvertFloatToMonoInt16(byte[] floatBytes, int channels)
    {
        if (channels <= 0)
            return Array.Empty<byte>();

        var frameCount = floatBytes.Length / 4 / channels;
        if (frameCount <= 0)
            return Array.Empty<byte>();

        var floatSamples = MemoryMarshal.Cast<byte, float>(floatBytes);
        var monoInt16 = new byte[frameCount * 2];

        for (var i = 0; i < frameCount; i++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
                sum += floatSamples[i * channels + c];

            var monoSample = Mathf.Clamp(sum / channels, -1f, 1f);
            var int16Sample = (short)(monoSample * short.MaxValue);

            monoInt16[i * 2] = (byte)(int16Sample & 0xFF);
            monoInt16[i * 2 + 1] = (byte)((int16Sample >> 8) & 0xFF);
        }

        return monoInt16;
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
