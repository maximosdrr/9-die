using Godot;
using Godot.Collections;

[GlobalClass]
public partial class AuthorityStateSynchronizer : Node
{
    public StateMachine StateMachine;
    public bool IsIncomingNetworkChange = false;

    public void Setup(StateMachine stateMachine)
    {
        StateMachine = stateMachine;

        if (Multiplayer.IsServer())
            Multiplayer.PeerConnected += OnClientConnect;

        StateMachine.StateChanged += OnLocalStateChange;
    }

    public override void _ExitTree()
    {
        if (StateMachine != null)
            StateMachine.StateChanged -= OnLocalStateChange;

        if (Multiplayer.IsServer())
            Multiplayer.PeerConnected -= OnClientConnect;
    }

    private void OnClientConnect(long peerId)
    {
        if (Multiplayer.IsServer())
            RpcId(peerId, MethodName.RemoteSyncState, StateMachine.Current.Type, StateMachine.CurrentMetadata);
    }

    private void OnLocalStateChange(string type, Dictionary metadata)
    {
        if (IsIncomingNetworkChange)
            return;

        if (!IsMultiplayerAuthority())
            return;

        if (Multiplayer.IsServer())
            Rpc(MethodName.RemoteSyncState, type, metadata);
        else
            RpcId(1, MethodName.RemoteSyncState, type, metadata);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RemoteSyncState(string type, Dictionary metadata)
    {
        var senderId = Multiplayer.GetRemoteSenderId();

        // Every peer, including clients, accepts state only from this state machine's authority.
        // The previous check protected the server but let one client forge state on another.
        if (senderId != GetMultiplayerAuthority())
        {
            GD.PushWarning($"Peer {senderId} attempted to change state without authority.");
            return;
        }

        IsIncomingNetworkChange = true;
        StateMachine.ChangeState(type, metadata);
        IsIncomingNetworkChange = false;

        if (Multiplayer.IsServer())
        {
            foreach (var peerId in Multiplayer.GetPeers())
            {
                if (peerId != senderId)
                    RpcId(peerId, MethodName.RemoteSyncState, type, metadata);
            }
        }
    }
}
