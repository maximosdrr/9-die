using Godot;
using Pool.Simulation;

/// <summary>
/// Carries a shot from whoever struck it to the server, which validates it and then hands it to
/// the simulation runner to broadcast.
///
/// What travels is the ShotInput's five numbers, not an impulse vector — so the server can bound
/// every one of them, and so every peer can reproduce the identical shot from the same
/// description. The old path sent a raw direction the server accepted unconditionally, which let
/// a modified client aim straight down and jump past the elevation limit, and clamped force to
/// 3.6x what an honest client could produce.
/// </summary>
[GlobalClass]
public partial class CueNetworkBridge : Node
{
    public Cue Cue;

    public void Setup(Cue cue)
    {
        Cue = cue;
        Cue.StrikeExecuted += CallStrike;
    }

    private void CallStrike(float aimYaw, float elevation, float speed, float tipOffsetX, float tipOffsetY)
    {
        // The striker may be the host. Rather than special-casing that with a separate code path
        // (which is how the server used to skip validation entirely), route both through the same
        // check — locally when we are already the authority, over the wire otherwise.
        if (Multiplayer.IsServer())
            TryStrike(Multiplayer.GetUniqueId(), aimYaw, elevation, speed, tipOffsetX, tipOffsetY);
        else
            RpcId(1, MethodName.RequestStrike, aimYaw, elevation, speed, tipOffsetX, tipOffsetY);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RequestStrike(float aimYaw, float elevation, float speed, float tipOffsetX, float tipOffsetY)
    {
        if (!Multiplayer.IsServer())
            return;

        TryStrike(Multiplayer.GetRemoteSenderId(), aimYaw, elevation, speed, tipOffsetX, tipOffsetY);
    }

    private void TryStrike(int requesterId, float aimYaw, float elevation, float speed, float tipOffsetX, float tipOffsetY)
    {
        if (requesterId != GetMultiplayerAuthority())
            return;

        if (Cue.PoolGame?.TurnOwner == null || (string)Cue.PoolGame.TurnOwner.Name != requesterId.ToString())
            return;

        var runner = Cue.PoolGame.SimulationRunner;

        // A shot arriving while the table is still settling would stack on top of the one in
        // flight. The old code had no such check — only a client-side cooldown, which a modified
        // client simply would not run.
        if (runner.IsPlaying)
            return;

        // Sanitized bounds speed, elevation and tip offset, so an out-of-range request becomes a
        // legal shot rather than an exploit or a rejection.
        var shot = new ShotInput(aimYaw, elevation, speed, tipOffsetX, tipOffsetY).Sanitized();
        if (shot.Speed <= 0.0)
            return;

        runner.BroadcastShot(shot);
    }
}
