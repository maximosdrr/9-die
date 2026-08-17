using System;
using Godot;

[GlobalClass]
public partial class TvScreenShare : Node3D
{
    private const int CaptureWidth = 1280;
    private const int CaptureHeight = 720;
    // Capture only needs modest headroom over the 20 fps network cadence. Capturing and WebP-
    // encoding 60 frames merely spent CPU on frames LatestFrameSlot would replace before they
    // could ever be sent, which could starve the main thread in visually heavy scenes.
    private const double TargetFps = 30.0;
    private const int FrameTransferChannel = 1;
    private const int AudioTransferChannel = 2;
    private const int AudioMixRate = 48000;
    private const int AudioPacketDurationMs = 10;
    private const int AudioPacketBytes = AudioMixRate * sizeof(short) * AudioPacketDurationMs / 1000;
    private const int AudioPacketsPerSecond = 1000 / AudioPacketDurationMs;
    private const int MaxPendingAudioPackets = 12;
    private const int MaxAudioPacketsPerFrame = 8;
    private const int MaxEncodedFrameBytes = 512 * 1024;
    private const int MaxAudioChunkBytes = AudioMixRate * sizeof(short);
    private const int MaxVideoPacketsPerSecond = 24;
    private const int MaxAudioBytesPerSecond = MaxAudioChunkBytes * 2;
    private const int ServerPeerId = 1;
    private const int MaxSteamPacketsPerChannelPerFrame = 16;
    private const int MaxSteamBytesPerChannelPerFrame = 1024 * 1024;

    internal static int SteamDrainPacketBudget => MaxSteamPacketsPerChannelPerFrame;
    internal static int SteamDrainByteBudget => MaxSteamBytesPerChannelPerFrame;

    // Steam's P2P transport gives SteamMultiplayerPeer only one real connection per remote peer,
    // so even distinct Godot TransferChannels still share its send/receive queue underneath —
    // a queued frame can delay unrelated gameplay RPCs behind it. On Steam we bypass
    // MultiplayerApi entirely for video/audio and talk to each peer's Steam ID directly over
    // its own P2P session (still relayed through the host, same star topology as the RPC path),
    // so media is not queued through MultiplayerApi's gameplay RPC channel. The provider can
    // still share lower-level resources, therefore send and receive work remains explicitly
    // bounded. ENet already gives independent channels, so it keeps the RPC path below.
    private const int SteamP2PVideoChannel = 10;
    private const int SteamP2PAudioChannel = 11;

    // Video frames are big (~60-100 KB of WebP) and individually disposable — a stale frame is
    // worthless the moment a newer one exists. Reliable-with-buffering never drops anything, so
    // when a peer's link can't keep up with the encoder, Steam's per-peer send queue just grows
    // without bound and playback slides seconds behind real time. Before queueing a payload for
    // a peer, peek at how much is still waiting in that peer's queue and skip them this round
    // if it's above the payload's threshold. Each viewer's frame rate then settles at whatever
    // their own link actually sustains, always showing the freshest frame, with latency bounded
    // instead of compounding.
    //
    // The fixed 128 KiB gate predates the detailed poker scene. Real 720p frames from that scene
    // now vary between roughly 80 and 160 KiB, so a single ordinary frame could put the queue
    // over the old limit and make the following frames look like a slide show. Keep a short,
    // payload-relative budget instead: enough for three current frames, but still bounded so a
    // slow viewer cannot accumulate seconds of stale video. Audio keeps its fixed gate.
    //
    // Audio gates too, but at 4x the old video floor: a dropped chunk is an
    // audible gap, so it only happens once the link is so far gone (queue already seconds deep)
    // that the alternative is audio drifting endlessly behind — at that point a stutter that
    // stays live beats a clean stream narrating the past.
    private const long VideoQueueLimitBytes = 128 * 1024;
    private const int VideoQueueFrameBudget = 1;

    internal static long SteamVideoQueueLimitForPayload(int payloadBytes)
    {
        var safePayloadBytes = Math.Clamp(payloadBytes, 1, MaxEncodedFrameBytes);
        return Math.Max(VideoQueueLimitBytes,
            (long)safePayloadBytes * VideoQueueFrameBudget);
    }

    // How often video frames are *sent*, decoupled from how fast capture+encode runs locally.
    // The sharer's own TV happily displays every captured frame, but pushing 35+ fps over the
    // network triples bandwidth for a gain nobody can perceive on an in-game TV; 20 fps is the
    // ceiling, and congestion gating above takes each peer below that as needed.
    private const ulong MinimumNetworkFrameIntervalMs = 50;
    private const long TargetVideoBytesPerSecond = 1024 * 1024;
    private ulong _lastVideoSendMs;

    internal static double CaptureTargetFps => TargetFps;
    internal static double NetworkTargetFps => 1000.0 / MinimumNetworkFrameIntervalMs;

    internal static ulong NetworkFrameIntervalForPayload(int payloadBytes)
    {
        var safePayloadBytes = Math.Clamp(payloadBytes, 1, MaxEncodedFrameBytes);
        var bitrateIntervalMs = (ulong)Math.Ceiling(
            safePayloadBytes * 1000.0 / TargetVideoBytesPerSecond);
        return Math.Max(MinimumNetworkFrameIntervalMs, bitrateIntervalMs);
    }

    [Signal]
    public delegate void SharerChangedEventHandler(int sharerId);

    [ExportGroup("Scene References")]
    [Export] public MeshInstance3D ScreenMesh;
    [Export] public AudioStreamPlayer3D AudioPlayer;
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
    private IAudioCaptureSource _audioCapture;
    private readonly System.Collections.Generic.Queue<byte[]> _pendingAudioPackets = new();
    private double _audioPacketSendBudget;
    private bool _audioFallbackWarningIssued;
    private ScreenCaptureWorker _videoCapture;
    private IntPtr _pendingCaptureWindow;
    private VideoPlayoutBuffer _playoutBuffer;

    private SteamNetworkProvider _steamProvider;
    private GodotObject _steam;
    private long _p2pSendReliableWithBuffering;
    private long _p2pSendUnreliableNoDelay;
    private readonly System.Collections.Generic.Dictionary<int, MediaRateWindow> _mediaRateByPeer = new();
    private bool _sendQueueTelemetryVerified;

    // Once-per-second diagnostics. These counters deliberately live on the main thread: besides
    // being safe for Godot's logger, they report what actually crossed each stage rather than the
    // theoretical capture target. This is kept cheap enough to remain enabled in exported builds.
    private ulong _fpsWindowStartedAtMs;
    private int _capturedFramesInWindow;
    private int _networkFramesInWindow;
    private int _receivedFramesInWindow;
    private int _displayedFramesInWindow;
    private int _steamVideoPacketsInWindow;
    private int _congestionDropsInWindow;
    private int _sendFailuresInWindow;
    private int _audioPacketsSentInWindow;
    private int _audioPacketsReceivedInWindow;
    private int _audioPacketsPlayedInWindow;
    private int _audioPlaybackDropsInWindow;
    private long _videoBytesInWindow;
    private int _videoSamplesInWindow;

    public override void _ExitTree()
    {
        DisconnectSignals();
        StopLocalCapture();

        var provider = NetworkManager.Instance?.NetworkProvider;
        if (provider != null)
        {
            provider.PlayerConnected -= OnPlayerConnected;
            provider.PlayerDisconnected -= OnPlayerDisconnected;
            provider.ServerDisconnected -= OnServerSessionDisconnected;
        }

        _playoutBuffer?.Clear();
        _playoutBuffer = null;
        _mediaRateByPeer.Clear();
        _shareControlLimiter.Clear();

        if (_audioPlayer != null)
            StopAudioPlayback(releaseStream: true);

        ClearScreen();

        if (_steam != null)
            SignalUtil.DisconnectGuarded(_steam, "p2p_session_request", new Callable(this, MethodName.OnP2PSessionRequest));
    }

    public override void _Ready()
    {
        _screenMeshInstance = ScreenMesh;
        _screenMaterial = (StandardMaterial3D)_screenMeshInstance.MaterialOverride;
        _screenMaterial.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;

        _audioPlayer = AudioPlayer;
        var generator = new AudioStreamGenerator
        {
            MixRate = AudioMixRate,
            // Absorbs normal scheduling jitter without creating a second audio delay.
            BufferLength = 0.15f,
        };
        _audioPlayer.Stream = generator;

        InteractionPrompt.Hide();

        // Deliberately *not* calling SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) on our own
        // window here. It does stop the TV-showing-itself feedback loop when a player shares their
        // whole primary screen, but the flag is enforced by the compositor for every capture path,
        // not just ours — the game window also comes out black/absent in Print Screen, Win+Shift+S,
        // Steam's F12, OBS and Discord. Never being able to screenshot the game is a worse trade
        // than a mirror effect that only appears while someone is sharing their full screen.
        if (IsAvailable)
            ConnectSignals();

        NetworkManager.Instance.NetworkProvider.PlayerConnected += OnPlayerConnected;
        NetworkManager.Instance.NetworkProvider.PlayerDisconnected += OnPlayerDisconnected;
        NetworkManager.Instance.NetworkProvider.ServerDisconnected += OnServerSessionDisconnected;

        _steamProvider = NetworkManager.Instance.NetworkProvider as SteamNetworkProvider;
        if (_steamProvider != null && Engine.HasSingleton("Steam"))
        {
            _steam = Engine.GetSingleton("Steam");
            _p2pSendReliableWithBuffering = _steam.Get("P2P_SEND_RELIABLE_WITH_BUFFERING").AsInt64();
            _p2pSendUnreliableNoDelay = _steam.Get("P2P_SEND_UNRELIABLE_NO_DELAY").AsInt64();
            SignalUtil.ConnectGuarded(_steam, "p2p_session_request", new Callable(this, MethodName.OnP2PSessionRequest));
            _steam.Call("allowP2PPacketRelay", true);
        }
        else
        {
            _steamProvider = null;
        }
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

    public override void _Process(double delta)
    {
        ProcessSteamIncoming();
        ProcessVideoCapture();
        ProcessPlayout();
        ProcessAudioCapture(delta);
        ReportScreenShareFps();
    }

    private void ResetScreenShareFps()
    {
        _fpsWindowStartedAtMs = Time.GetTicksMsec();
        _capturedFramesInWindow = 0;
        _networkFramesInWindow = 0;
        _receivedFramesInWindow = 0;
        _displayedFramesInWindow = 0;
        _steamVideoPacketsInWindow = 0;
        _congestionDropsInWindow = 0;
        _sendFailuresInWindow = 0;
        _audioPacketsSentInWindow = 0;
        _audioPacketsReceivedInWindow = 0;
        _audioPacketsPlayedInWindow = 0;
        _audioPlaybackDropsInWindow = 0;
        _videoBytesInWindow = 0;
        _videoSamplesInWindow = 0;
    }

    private void ReportScreenShareFps()
    {
        if (SharerId == 0)
            return;

        var nowMs = Time.GetTicksMsec();
        if (_fpsWindowStartedAtMs == 0)
            _fpsWindowStartedAtMs = nowMs;

        var elapsedMs = nowMs - _fpsWindowStartedAtMs;
        if (elapsedMs < 1_000)
            return;

        var seconds = elapsedMs / 1000.0;
        var role = SharerId == Multiplayer.GetUniqueId() ? "origem" : "viewer";
        var averageKiB = _videoSamplesInWindow > 0
            ? _videoBytesInWindow / (double)_videoSamplesInWindow / 1024.0
            : 0.0;
        var bufferedFrames = _playoutBuffer?.BufferedFrameCount ?? 0;

        GD.Print(
            $"[TvScreenShare/FPS] {role} | captura {_capturedFramesInWindow / seconds:F1} | "
            + $"envio {_networkFramesInWindow / seconds:F1} | "
            + $"recebimento {_receivedFramesInWindow / seconds:F1} | "
            + $"exibição {_displayedFramesInWindow / seconds:F1} | "
            + $"frame {averageKiB:F0} KiB | buffer {bufferedFrames} | "
            + $"Steam enviados {_steamVideoPacketsInWindow}, congestionados {_congestionDropsInWindow}, "
            + $"falhas {_sendFailuresInWindow} | áudio env {_audioPacketsSentInWindow}, "
            + $"rec {_audioPacketsReceivedInWindow}, play {_audioPacketsPlayedInWindow}, "
            + $"drop {_audioPlaybackDropsInWindow}");

        ResetScreenShareFps();
    }
}
