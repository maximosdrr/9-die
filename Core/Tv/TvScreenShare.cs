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

	// Steam's P2P transport gives SteamMultiplayerPeer only one real connection per remote peer,
	// so even distinct Godot TransferChannels still share its send/receive queue underneath —
	// a queued frame can delay unrelated gameplay RPCs behind it. On Steam we bypass
	// MultiplayerApi entirely for video/audio and talk to each peer's Steam ID directly over
	// its own P2P session (still relayed through the host, same star topology as the RPC path),
	// so a saturated stream can never back up gameplay traffic. ENet already gives independent
	// channels for free, so it keeps using the RPC path below unchanged.
	private const int SteamP2PVideoChannel = 10;
	private const int SteamP2PAudioChannel = 11;

	[Signal]
	public delegate void SharerChangedEventHandler(int sharerId);

	[Export] public Area3D InteractionArea;
	[Export] public PoolStartGameUI InteractionPrompt;

	public static bool IsAvailable => OS.GetName() == "Windows";

	public int SharerId { get; private set; } = 0;
	public ImageTexture Texture => _texture;
	public bool IsLocalPlayerInRange { get; private set; }

	private bool _promptSuppressed;

	private MeshInstance3D _screenMeshInstance;
	private StandardMaterial3D _screenMaterial;
	private ImageTexture _texture;

	private AudioStreamPlayer3D _audioPlayer;
	private AudioStreamGeneratorPlayback _audioPlayback;
	private SystemAudioCapture _audioCapture;
	private ScreenCaptureWorker _videoCapture;
	private IntPtr _pendingCaptureWindow;
	private VideoPlayoutBuffer _playoutBuffer;

	private SteamNetworkProvider _steamProvider;
	private GodotObject _steam;
	private long _p2pSendReliableWithBuffering;

	public override void _ExitTree()
	{
		DisconnectSignals();
		StopLocalCapture();

		if (_steam != null)
			SignalUtil.DisconnectGuarded(_steam, "p2p_session_request", new Callable(this, MethodName.OnP2PSessionRequest));
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

		InteractionPrompt.Hide();

		if (IsAvailable)
		{
			ConnectSignals();

			var hwnd = (IntPtr)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, (int)DisplayServer.MainWindowId);
			WindowsScreenCapture.ExcludeWindowFromCapture(hwnd);
		}

		NetworkManager.Instance.NetworkProvider.PlayerConnected += OnPlayerConnected;
		NetworkManager.Instance.NetworkProvider.PlayerDisconnected += OnPlayerDisconnected;

		_steamProvider = NetworkManager.Instance.NetworkProvider as SteamNetworkProvider;
		if (_steamProvider != null)
		{
			_steam = Engine.GetSingleton("Steam");
			_p2pSendReliableWithBuffering = _steam.Get("P2P_SEND_RELIABLE_WITH_BUFFERING").AsInt64();
			SignalUtil.ConnectGuarded(_steam, "p2p_session_request", new Callable(this, MethodName.OnP2PSessionRequest));
			_steam.Call("allowP2PPacketRelay", true);
		}
	}

	// Steam requires explicitly accepting an incoming P2P session the first time a remote peer
	// sends us anything on this API, otherwise the packets are silently dropped.
	private void OnP2PSessionRequest(ulong remoteSteamId)
	{
		_steam.Call("acceptP2PSessionWithUser", remoteSteamId);
	}

	private void ConnectSignals()
	{
		SignalUtil.ConnectGuarded(InteractionArea, Area3D.SignalName.BodyEntered, new Callable(this, MethodName.OnBodyEnteredInteractionArea));
		SignalUtil.ConnectGuarded(InteractionArea, Area3D.SignalName.BodyExited, new Callable(this, MethodName.OnBodyExitedInteractionArea));
	}

	private void DisconnectSignals()
	{
		SignalUtil.DisconnectGuarded(InteractionArea, Area3D.SignalName.BodyEntered, new Callable(this, MethodName.OnBodyEnteredInteractionArea));
		SignalUtil.DisconnectGuarded(InteractionArea, Area3D.SignalName.BodyExited, new Callable(this, MethodName.OnBodyExitedInteractionArea));
	}

	private void OnBodyEnteredInteractionArea(Node3D body)
	{
		if (body is Player player && player.IsMultiplayerAuthority())
		{
			IsLocalPlayerInRange = true;
			UpdateInteractionPrompt();
		}
	}

	private void OnBodyExitedInteractionArea(Node3D body)
	{
		if (body is Player player && player.IsMultiplayerAuthority())
		{
			IsLocalPlayerInRange = false;
			UpdateInteractionPrompt();
		}
	}

	// Called by TvShareButton whenever it opens/closes the source picker or the viewing overlay
	// — while either is open, the local player already has the corresponding UI in front of
	// them, so the world-space prompt offering that same action would just be redundant.
	public void SetPromptSuppressed(bool suppressed)
	{
		_promptSuppressed = suppressed;
		UpdateInteractionPrompt();
	}

	private void UpdateInteractionPrompt()
	{
		if (!IsLocalPlayerInRange || _promptSuppressed)
		{
			InteractionPrompt.Hide();
			return;
		}

		var isLocalSharer = SharerId != 0 && SharerId == Multiplayer.GetUniqueId();

		if (isLocalSharer)
		{
			InteractionPrompt.SetText("Pressione F para parar de compartilhar");
			InteractionPrompt.Show();
		}
		else if (SharerId == 0)
		{
			InteractionPrompt.SetText("Pressione F para compartilhar a tela");
			InteractionPrompt.Show();
		}
		else
		{
			InteractionPrompt.SetText("Pressione F para assistir em tela cheia");
			InteractionPrompt.Show();
		}
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

			// AudioStreamGeneratorPlayback refuses to clear its ring buffer while actively
			// playing (Godot's clear_buffer() asserts !active), so the player has to be
			// stopped first and restarted afterwards so it keeps accepting pushes for the
			// next viewing session. Play() may hand back a new playback instance, so it's
			// re-fetched rather than assumed to be the same object.
			_audioPlayer.Stop();
			_audioPlayback?.ClearBuffer();
			_audioPlayer.Play();
			_audioPlayback = (AudioStreamGeneratorPlayback)_audioPlayer.GetStreamPlayback();
		}

		var isLocalSharer = sharerId != 0 && sharerId == Multiplayer.GetUniqueId();
		var isRemoteViewer = sharerId != 0 && !isLocalSharer;

		if (isLocalSharer)
			StartLocalCapture();
		else
			StopLocalCapture();

		if (isRemoteViewer)
		{
			_playoutBuffer ??= new VideoPlayoutBuffer();
		}
		else
		{
			_playoutBuffer?.Clear();
			_playoutBuffer = null;
		}

		UpdateInteractionPrompt();

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
		ProcessSteamIncoming();
		ProcessVideoCapture();
		ProcessPlayout();
		ProcessAudioCapture();
	}

	// Drains both raw Steam P2P channels. Only relevant when running over Steam — over ENet,
	// incoming video/audio still arrives through the ordinary RPC methods below.
	private void ProcessSteamIncoming()
	{
		if (_steamProvider == null)
			return;

		DrainSteamChannel(SteamP2PVideoChannel, isAudio: false);
		DrainSteamChannel(SteamP2PAudioChannel, isAudio: true);
	}

	private void DrainSteamChannel(int channel, bool isAudio)
	{
		while (true)
		{
			var availableSize = _steam.Call("getAvailableP2PPacketSize", channel).AsInt32();
			if (availableSize <= 0)
				return;

			var packet = _steam.Call("readP2PPacket", availableSize, channel).AsGodotDictionary();

			// getAvailableP2PPacketSize can report a packet that readP2PPacket then fails to
			// produce (e.g. it hasn't fully landed yet) — that comes back as an empty
			// Dictionary, not an exception, so this has to be checked explicitly rather than
			// indexing straight into it. Bail out for this frame and let the next _Process
			// tick retry instead of spinning on the same stale size.
			if (!packet.ContainsKey("data") || !packet.ContainsKey("remote_steam_id"))
				return;

			var data = packet["data"].AsByteArray();
			var remoteSteamId = packet["remote_steam_id"].AsUInt64();

			if (Multiplayer.IsServer())
			{
				var senderId = _steamProvider.GetPeerId(remoteSteamId);
				if (senderId != SharerId)
					continue;

				if (isAudio)
					PlayAudioChunk(data);
				else
					_playoutBuffer?.Enqueue(data);

				foreach (var peerId in Multiplayer.GetPeers())
					if (peerId != senderId)
						SendSteamPacket(channel, peerId, data);
			}
			else
			{
				if (isAudio)
					PlayAudioChunk(data);
				else
					_playoutBuffer?.Enqueue(data);
			}
		}
	}

	// Mirrors the "server relays to every other peer, everyone else only talks to the host"
	// star topology the RPC path below uses, just addressed by Steam ID instead of Godot peer id.
	private void BroadcastSteamPacket(int channel, byte[] data)
	{
		if (Multiplayer.IsServer())
		{
			foreach (var peerId in Multiplayer.GetPeers())
				SendSteamPacket(channel, peerId, data);
		}
		else
		{
			SendSteamPacket(channel, 1, data);
		}
	}

	private void SendSteamPacket(int channel, int peerId, byte[] data)
	{
		var steamId = _steamProvider.GetSteamId(peerId);
		if (steamId == 0)
			return;

		_steam.Call("sendP2PPacket", steamId, data, _p2pSendReliableWithBuffering, channel);
	}

	private void ProcessPlayout()
	{
		if (_playoutBuffer == null || !_playoutBuffer.TryDequeueDue(out var encodedBytes))
			return;

		DisplayFrame(encodedBytes);
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

		if (_steamProvider != null)
		{
			BroadcastSteamPacket(SteamP2PVideoChannel, encodedBytes);
			return;
		}

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
		if (_steamProvider != null)
		{
			BroadcastSteamPacket(SteamP2PAudioChannel, monoInt16);
			return;
		}

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

		_playoutBuffer?.Enqueue(encodedBytes);

		foreach (var peerId in Multiplayer.GetPeers())
			if (peerId != senderId)
				RpcId(peerId, MethodName.SendFrame, encodedBytes);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = FrameTransferChannel)]
	private void SendFrame(byte[] encodedBytes)
	{
		_playoutBuffer?.Enqueue(encodedBytes);
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
