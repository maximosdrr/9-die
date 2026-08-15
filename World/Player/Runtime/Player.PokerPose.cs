using Godot;

/// <summary>Replicates the two stable poker card poses used by right-button peeking.</summary>
public partial class Player : CharacterBody3D
{
    // Godot reserves multiple ENet channels behind transfer channel zero. A logical channel six
    // therefore needs network/max_channels (and Steam's equivalent) to reserve at least six
    // application channels; see the matching project settings and contract test.
    internal const int PokerPoseTransferChannel = 6;
    private const int PokerPoseRequestsPerSecond = 8;
    private bool _pokerCardsRaised;
    private ulong _pokerPoseLockedUntilMilliseconds;
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
        TransferChannel = PokerPoseTransferChannel)]
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
        TransferChannel = PokerPoseTransferChannel)]
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
        // A public table gesture owns the arms until its authored one-shot is over. Reliable pose
        // packets use a separate channel and may arrive after the reveal/bet/check snapshot; letting
        // one of those packets play an idle here would interrupt the gesture on remote peers.
        if (Time.GetTicksMsec() < _pokerPoseLockedUntilMilliseconds)
        {
            _pokerCardsRaised = false;
            return false;
        }

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

    private void LockPokerPoseForGesture(string animationName, float duration)
    {
        if (animationName is not (PokerClips.BodyThrowChips
            or PokerClips.BodyKnock or PokerClips.BodyReveal))
        {
            return;
        }

        _pokerCardsRaised = false;
        var milliseconds = (ulong)Mathf.CeilToInt(Mathf.Max(duration, 0.1f) * 1000.0f);
        var lockedUntil = Time.GetTicksMsec() + milliseconds;
        if (lockedUntil > _pokerPoseLockedUntilMilliseconds)
            _pokerPoseLockedUntilMilliseconds = lockedUntil;
    }

    private void ClearPokerPoseRequests()
    {
        _pokerPoseRequestLimiter.Clear();
        _pokerPoseLockedUntilMilliseconds = 0;
    }
}
