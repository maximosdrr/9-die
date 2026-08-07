using Godot;
using Pool.Simulation;

/// <summary>
/// Carries a shot from the striking client to the server. What travels is the ShotInput's five
/// numbers, not an impulse — so the server can bound every one of them, and so both sides can
/// reproduce the identical shot from the same description.
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

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RequestStrike(float aimYaw, float elevation, float speed, float tipOffsetX, float tipOffsetY)
    {
        if (!Multiplayer.IsServer())
            return;

        var senderId = Multiplayer.GetRemoteSenderId();

        if (senderId != GetMultiplayerAuthority())
            return;

        if (Cue.PoolGame?.TurnOwner == null || (string)Cue.PoolGame.TurnOwner.Name != senderId.ToString())
            return;

        // A shot arriving while the table is still settling would stack on top of the one in
        // flight. The old code had no such check — only a client-side cooldown, which a modified
        // client simply would not run.
        if (Cue.PoolGame.SimulationRunner.IsPlaying)
            return;

        // Sanitized() bounds speed, elevation and tip offset, so an out-of-range request becomes
        // a legal shot rather than an exploit. The old validation clamped force to
        // ForceMultiplier, which was 3.6x what an honest client could actually produce.
        var shot = new ShotInput(aimYaw, elevation, speed, tipOffsetX, tipOffsetY).Sanitized();

        if (shot.Speed <= 0.0)
            return;

        Cue.PoolGame.SimulationRunner.ExecuteShot(shot);
    }

    private void CallStrike(float aimYaw, float elevation, float speed, float tipOffsetX, float tipOffsetY)
    {
        RpcId(1, MethodName.RequestStrike, aimYaw, elevation, speed, tipOffsetX, tipOffsetY);
    }
}
