using System;
using Godot;

public partial class TvScreenShare
{
    internal const int ShareControlRequestsPerSecond = 4;

    private readonly PeerRequestRateLimiter _shareControlLimiter = new(
        ShareControlRequestsPerSecond,
        windowMilliseconds: 1_000,
        maxTrackedPeers: 16);

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

        var requesterId = Multiplayer.GetRemoteSenderId();
        if (_shareControlLimiter.TryConsume(requesterId))
            TryStartShare(requesterId);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestStopShare()
    {
        if (!Multiplayer.IsServer())
            return;

        var requesterId = Multiplayer.GetRemoteSenderId();
        if (_shareControlLimiter.TryConsume(requesterId))
            TryStopShare(requesterId);
    }

    private void TryStartShare(int requesterId)
    {
        // The source picker can only be opened by the local player while their own instance says
        // they are in range. Rechecking GetOverlappingBodies on the host rejected legitimate
        // clients whenever their replicated collision had not entered the host's Area3D yet.
        // Authenticate the RPC by its connected peer ID instead; rate limiting remains in place.
        if (SharerId != 0 || !IsKnownSessionPeer(requesterId))
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

    internal bool TryConsumeShareControlRequest(int peerId, ulong nowMilliseconds) =>
        _shareControlLimiter.TryConsume(peerId, nowMilliseconds);

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
        _mediaRateByPeer.Clear();

        if (sharerId == 0)
        {
            ClearScreen();
            StopAudioPlayback();
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

    private void OnServerSessionDisconnected()
    {
        // NetworkProvider has already released its peer when this signal fires. Stop native
        // capture synchronously so the deferred scene reload cannot leave a producer trying to
        // send through a disposed ENet/Steam peer during that one-frame window.
        ApplySharerChange(0);
        _pendingCaptureWindow = IntPtr.Zero;
    }
}
