using Godot;
using NAudio.Wave;
using System;
using System.IO;
using System.Runtime.InteropServices;

[GlobalClass]
public partial class TvScreenShare : MeshInstance3D
{
	private const int CaptureWidth = 1280;
	private const int CaptureHeight = 720;
	private const double TargetFps = 60.0;
	private const int FrameTransferChannel = 1;
	private const int AudioTransferChannel = 2;
	private const int AudioMixRate = 48000;

	[Signal]
	public delegate void SharerChangedEventHandler(int sharerId);

	public int SharerId { get; private set; } = 0;
	public ImageTexture Texture => _texture;

	private MeshInstance3D _screenMeshInstance;
	private StandardMaterial3D _screenMaterial;
	private ImageTexture _texture;

	private AudioStreamPlayer3D _audioPlayer;
	private AudioStreamGeneratorPlayback _audioPlayback;
	private SystemAudioCapture _audioCapture;
	private ScreenCaptureWorker _videoCapture;
	private IntPtr _pendingCaptureWindow;

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
		_screenMaterial.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;

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

		if (OS.GetName() == "Windows")
		{
			var hwnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, (int)DisplayServer.MainWindowId);
			WindowsScreenCapture.ExcludeWindowFromCapture(hwnd);
		}

		NetworkManager.Instance.NetworkProvider.PlayerConnected += OnPlayerConnected;
		NetworkManager.Instance.NetworkProvider.PlayerDisconnected += OnPlayerDisconnected;
	}

	public void RequestStartSharing(IntPtr windowHandle = default)
	{
		// Which window (if any) is purely local to this machine's capture step — it never needs
		// to travel over the network, so it's just stashed here for StartLocalCapture to pick up
		// once the host confirms this peer as the sharer.
		_pendingCaptureWindow = windowHandle;

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
		if (_videoCapture == null)
			_videoCapture = new ScreenCaptureWorker(CaptureWidth, CaptureHeight, TargetFps, _pendingCaptureWindow);

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
		_videoCapture?.Dispose();
		_videoCapture = null;

		if (_audioCapture == null)
			return;

		_audioCapture.Stop();
		_audioCapture.Dispose();
		_audioCapture = null;
	}

	public override void _Process(double delta)
	{
		ProcessVideoCapture();
		ProcessAudioCapture();
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

		DisplayFrame(encodedBytes);

		if (Multiplayer.IsServer())
		{
			foreach (var peerId in Multiplayer.GetPeers())
				RpcId(peerId, MethodName.SendFrame, encodedBytes);
		}
		else
		{
			RpcId(1, MethodName.SubmitFrame, encodedBytes);
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

		// Deliberately not calling PlayAudioChunk here: the sharer already hears this audio
		// directly from their own system output, so looping it back through the TV speaker
		// would double it up as an echo. Only relay it to everyone else.
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
	private void SubmitFrame(byte[] encodedBytes)
	{
		if (!Multiplayer.IsServer())
			return;

		var senderId = Multiplayer.GetRemoteSenderId();
		if (senderId != SharerId)
			return;

		DisplayFrame(encodedBytes);

		foreach (var peerId in Multiplayer.GetPeers())
			if (peerId != senderId)
				RpcId(peerId, MethodName.SendFrame, encodedBytes);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = FrameTransferChannel)]
	private void SendFrame(byte[] encodedBytes)
	{
		DisplayFrame(encodedBytes);
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

	private void DisplayFrame(byte[] encodedBytes)
	{
		var image = new Image();
		var err = image.LoadWebpFromBuffer(encodedBytes);
		if (err != Error.Ok)
			return;

		// Without mipmaps, viewing this texture on a small/distant 3D surface (or at a steep
		// angle) causes shimmering/aliasing since the GPU can't minify it properly.
		image.GenerateMipmaps();

		if (_texture == null)
		{
			_texture = ImageTexture.CreateFromImage(image);
			_screenMaterial.AlbedoColor = Colors.White;
			_screenMaterial.AlbedoTexture = _texture;
		}
		else
		{
			_texture.Update(image);
		}
	}

	private void ClearScreen()
	{
		// The screen material is Unshaded, so AlbedoTexture alone is enough to display the
		// captured frame at full brightness; Emission was only adding a second, unnecessary
		// brightness pass that pushed the scene's global glow/bloom into blowing out bright
		// desktop content (white backgrounds, text) into a hazy halo.
		_texture = null;
		_screenMaterial.AlbedoColor = new Color(0.05f, 0.05f, 0.05f);
		_screenMaterial.AlbedoTexture = null;
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
