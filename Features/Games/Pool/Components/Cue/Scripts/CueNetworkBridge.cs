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
        Cue.CueBall.Strike(dir, finalForce, hitOffset);
    }

    private void CallStrike(Vector3 dir, float finalForce, Vector3 hitOffset)
    {
        RpcId(1, MethodName.RequestStrike, dir, finalForce, hitOffset);
    }
}
