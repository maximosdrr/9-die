using Godot;
using Pool.Simulation;

/// <summary>
/// Carries a player's normalized shot intent to the server. The server validates every field,
/// computes the authoritative cue speed from its Cue configuration and tells the requester whether
/// the command was accepted.
/// </summary>
[GlobalClass]
public partial class CueNetworkBridge : Node
{
    internal const int ServerPeerId = 1;
    internal const int StrikeRequestsPerSecond = 4;
    internal const int MaximumTrackedStrikePeers = 8;

    [Signal]
    public delegate void ShotRequestResolvedEventHandler(int sequence, bool accepted, string reason);

    public Cue Cue;
    private int _lastAcceptedSequence;
    private readonly PeerRequestRateLimiter _strikeRequestLimiter = new(
        StrikeRequestsPerSecond, 1000, MaximumTrackedStrikePeers);

    public void Setup(Cue cue)
    {
        _strikeRequestLimiter.Clear();
        Cue = cue;
        Cue.StrikeExecuted += CallStrike;
    }

    private void CallStrike(int sequence, float aimYaw, float elevation, float normalizedPower,
        float tipOffsetX, float tipOffsetY)
    {
        if (Multiplayer.IsServer())
            TryStrike(Multiplayer.GetUniqueId(), sequence, aimYaw, elevation, normalizedPower,
                tipOffsetX, tipOffsetY);
        else
            RpcId(ServerPeerId, MethodName.RequestStrike, sequence, aimYaw, elevation, normalizedPower,
                tipOffsetX, tipOffsetY);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RequestStrike(int sequence, float aimYaw, float elevation, float normalizedPower,
        float tipOffsetX, float tipOffsetY)
    {
        if (!Multiplayer.IsServer())
            return;

        var requesterId = Multiplayer.GetRemoteSenderId();
        if (!TryConsumeStrikeRequest(requesterId))
            return;

        TryStrike(requesterId, sequence, aimYaw, elevation, normalizedPower,
            tipOffsetX, tipOffsetY);
    }

    private bool TryConsumeStrikeRequest(int requesterId) =>
        _strikeRequestLimiter.TryConsume(requesterId);

    internal bool TryConsumeStrikeRequest(int requesterId, ulong nowMilliseconds) =>
        _strikeRequestLimiter.TryConsume(requesterId, nowMilliseconds);

    private void TryStrike(int requesterId, int sequence, float aimYaw, float elevation,
        float normalizedPower, float tipOffsetX, float tipOffsetY)
    {
        if (requesterId != GetMultiplayerAuthority())
        {
            ResolveRequest(requesterId, sequence, false, "not_authorized");
            return;
        }

        if (Cue.PoolGame == null || !Cue.PoolGame.IsMatchActive
            || !Cue.PoolGame.IsTurnOwner(requesterId.ToString()))
        {
            ResolveRequest(requesterId, sequence, false, "not_your_turn");
            return;
        }

        var runner = Cue.PoolGame.SimulationRunner;
        var resolver = Cue.PoolGame.GameModeHandler?.CurrentGameMode?.TurnResolver as PoolTurnResolver;
        if ((resolver != null && resolver.IsShotBlocked)
            || Cue.PoolGame.BallPlacementManager.IsPlacementPendingFor(requesterId.ToString()))
        {
            ResolveRequest(requesterId, sequence, false, "turn_action_pending");
            return;
        }

        if (runner.IsPlaying)
        {
            ResolveRequest(requesterId, sequence, false, "table_in_motion");
            return;
        }

        if (sequence <= _lastAcceptedSequence)
        {
            ResolveRequest(requesterId, sequence, false, "stale_sequence");
            return;
        }

        if (!float.IsFinite(aimYaw) || !float.IsFinite(elevation)
            || !float.IsFinite(normalizedPower) || !float.IsFinite(tipOffsetX)
            || !float.IsFinite(tipOffsetY))
        {
            ResolveRequest(requesterId, sequence, false, "invalid_values");
            return;
        }

        var safePower = Mathf.Clamp(normalizedPower, 0.0f, 1.0f);
        if (safePower <= Cue.MinPowerThreshold)
        {
            ResolveRequest(requesterId, sequence, false, "power_too_low");
            return;
        }

        var speed = Cue.PowerToCueSpeed(safePower, Cue.NormalCueSpeed,
            Mathf.Clamp(Cue.MaxCueSpeed, 0.0f, (float)ShotInput.MaxSpeed));
        var shot = new ShotInput(aimYaw, elevation, speed, tipOffsetX, tipOffsetY).Sanitized();
        if (shot.Speed <= 0.0)
        {
            ResolveRequest(requesterId, sequence, false, "invalid_speed");
            return;
        }

        if (!runner.BroadcastShot(shot))
        {
            ResolveRequest(requesterId, sequence, false, "simulation_rejected");
            return;
        }

        _lastAcceptedSequence = sequence;
        ResolveRequest(requesterId, sequence, true, "accepted");
    }

    private void ResolveRequest(int requesterId, int sequence, bool accepted, string reason)
    {
        if (requesterId == Multiplayer.GetUniqueId())
            EmitSignal(SignalName.ShotRequestResolved, sequence, accepted, reason);
        else
            RpcId(requesterId, MethodName.ReceiveShotResolution, sequence, accepted, reason);
    }

    // This node is owned by the player, not by the server. RpcMode.Authority would therefore
    // reject the server's acknowledgement on a remote client before this method could run.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveShotResolution(int sequence, bool accepted, string reason)
    {
        if (Multiplayer.GetRemoteSenderId() != ServerPeerId)
        {
            GD.PushWarning("Confirmação de tacada ignorada: remetente não é o servidor.");
            return;
        }

        EmitSignal(SignalName.ShotRequestResolved, sequence, accepted, reason);
    }
}
