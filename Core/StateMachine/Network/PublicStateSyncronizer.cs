using Godot;
using Godot.Collections;

[GlobalClass]
public partial class PublicStateSyncronizer : Node
{
    public StateMachine StateMachine;
    private bool _isIncomingNetworkChange = false;

    public void Setup(StateMachine stateMachine)
    {
        StateMachine = stateMachine;

        if (Multiplayer.IsServer())
            Multiplayer.PeerConnected += OnClientConnect;

        StateMachine.StateChanged += OnLocalStateChange;
    }

    private void OnClientConnect(long peerId)
    {
        if (Multiplayer.IsServer())
            RpcId(peerId, MethodName.RemoteSyncState, StateMachine.Current.Type, StateMachine.CurrentMetadata);
    }

    private void OnLocalStateChange(string type, Dictionary metadata)
    {
        if (_isIncomingNetworkChange)
            return;

        if (Multiplayer.IsServer())
            Rpc(MethodName.RemoteSyncState, type, metadata);
        else
            RpcId(1, MethodName.RemoteSyncState, type, metadata);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RemoteSyncState(string type, Dictionary metadata)
    {
        _isIncomingNetworkChange = true;

        StateMachine.ChangeState(type, metadata);

        _isIncomingNetworkChange = false;

        if (Multiplayer.IsServer())
        {
            var senderId = Multiplayer.GetRemoteSenderId();

            foreach (var peerId in Multiplayer.GetPeers())
            {
                if (peerId != senderId)
                    RpcId(peerId, MethodName.RemoteSyncState, type, metadata);
            }
        }
    }
}
