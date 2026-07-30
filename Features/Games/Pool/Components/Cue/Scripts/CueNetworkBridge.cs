using Godot;

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
    public void RequestStrike(Vector3 dir, float finalForce, Vector3 hitOffset)
    {
        if (!Multiplayer.IsServer())
            return;

        var senderId = Multiplayer.GetRemoteSenderId();

        if (senderId != GetMultiplayerAuthority())
            return;

        if (Cue.PoolGame?.TurnOwner == null || (string)Cue.PoolGame.TurnOwner.Name != senderId.ToString())
            return;

        if (!IsInstanceValid(Cue.CueBall))
            return;

        var safeDir = dir.Length() > 0.0001f ? dir.Normalized() : -Cue.GlobalTransform.Basis.Z.Normalized();
        var safeForce = Mathf.Clamp(finalForce, 0.0f, Cue.ForceMultiplier);
        var safeOffset = hitOffset.LimitLength(Cue.SpinLimit);

        Cue.CueBall.Strike(safeDir, safeForce, safeOffset);
    }

    private void CallStrike(Vector3 dir, float finalForce, Vector3 hitOffset)
    {
        RpcId(1, MethodName.RequestStrike, dir, finalForce, hitOffset);
    }
}
