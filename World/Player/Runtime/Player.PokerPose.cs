using Godot;

/// <summary>Replicates the two stable poker card poses used by right-button peeking.</summary>
public partial class Player : CharacterBody3D
{
    private const int PokerPoseChannel = 6;
    private const int PokerPoseRequestsPerSecond = 8;
    private bool _pokerCardsRaised;
    private readonly PeerRequestRateLimiter _pokerPoseRequestLimiter = new(
        PokerPoseRequestsPerSecond,
        windowMilliseconds: 1_000,
        maxTrackedPeers: 16);

    public void SetPokerCardLook(bool raised)
    {
        if (!IsMultiplayerAuthority() || !IsInSeatedGameMode)
            return;

        if (!ApplyPokerCardLook(raised))
            return;
        if (Multiplayer.MultiplayerPeer == null)
            return;

        if (Multiplayer.IsServer())
            BroadcastPokerCardLook(raised);
        else if (IsServerConnected())
            RpcId(PlayerMovementProtocol.ServerPeerId, MethodName.SubmitPokerCardLook, raised);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = PokerPoseChannel)]
    private void SubmitPokerCardLook(bool raised)
    {
        var senderId = Multiplayer.GetRemoteSenderId();
        if (!Multiplayer.IsServer()
            || senderId != Id)
        {
            return;
        }

        // Raising is rate-limited, but lowering is always allowed. Since unchanged states are
        // discarded below, allowing the release edge cannot amplify traffic and guarantees a
        // client can never leave the remote body stuck looking at its cards.
        if (raised && !_pokerPoseRequestLimiter.TryConsume(senderId))
            return;

        if (!ApplyPokerCardLook(raised))
            return;
        BroadcastPokerCardLook(raised);
    }

    private void BroadcastPokerCardLook(bool raised)
    {
        if (!Multiplayer.IsServer())
            return;

        foreach (var peerId in Multiplayer.GetPeers())
            RpcId(peerId, MethodName.ReceivePokerCardLook, raised);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,
        TransferChannel = PokerPoseChannel)]
    private void ReceivePokerCardLook(bool raised)
    {
        if (Multiplayer.IsServer()
            || Multiplayer.GetRemoteSenderId() != PlayerMovementProtocol.ServerPeerId)
        {
            return;
        }

        ApplyPokerCardLook(raised);
    }

    private bool ApplyPokerCardLook(bool raised)
    {
        if (_pokerCardsRaised == raised)
            return false;

        _pokerCardsRaised = raised;
        CharacterVisual?.Play(
            raised
                ? CharacterVisual.Clips.IdleSitHoldingCards
                : CharacterVisual.Clips.IdleHoldingCardsDown,
            0.18);
        return true;
    }

    private void ClearPokerPoseRequests() => _pokerPoseRequestLimiter.Clear();
}
