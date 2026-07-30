using Godot;
using Godot.Collections;

[GlobalClass]
public partial class TableTurnNetworkBridge : Node
{
    public TableGame TableGame;

    public void Setup(TableGame tableGame)
    {
        TableGame = tableGame;

        SignalUtil.ConnectGuarded(TableGame, TableGame.SignalName.MatchStarted, new Callable(this, MethodName.OnMatchStartedServerSide));
        SignalUtil.ConnectGuarded(TableGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChangedServerSide));
        SignalUtil.ConnectGuarded(TableGame, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtendedServerSide));
        SignalUtil.ConnectGuarded(TableGame, TableGame.SignalName.MatchOver, new Callable(this, MethodName.OnMatchOverServerSide));
    }

    private void OnMatchStartedServerSide(Array playersIds, string firstPlayer)
    {
        if (Multiplayer.IsServer())
            Rpc(MethodName.RpcSyncMatchSetup, playersIds, firstPlayer);
    }

    private void OnTurnChangedServerSide(string nextPlayerId, Dictionary context)
    {
        if (Multiplayer.IsServer())
            Rpc(MethodName.RpcSyncTurnUpdate, nextPlayerId, context);
    }

    private void OnTurnExtendedServerSide(Dictionary context)
    {
        if (Multiplayer.IsServer())
            Rpc(MethodName.RpcSyncTurnExtension, context);
    }

    private void OnMatchOverServerSide(string winner, Dictionary context)
    {
        if (Multiplayer.IsServer())
            Rpc(MethodName.RpcSyncMatchOver, winner, context);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcSyncMatchSetup(Array playersIds, string firstPlayer)
    {
        TableGame.SetupMatch(playersIds, firstPlayer);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcSyncTurnUpdate(string nextPlayerId, Dictionary context)
    {
        TableGame.ApplyNewTurn(nextPlayerId, context);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcSyncTurnExtension(Dictionary context)
    {
        TableGame.ApplyTurnExtension(context);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcSyncMatchOver(string winner, Dictionary context)
    {
        TableGame.ApplyMatchOver(winner, context);
    }
}
