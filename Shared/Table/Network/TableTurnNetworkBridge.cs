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
        SignalUtil.ConnectGuarded(TableGame, TableGame.SignalName.PlayerRemovedFromMatch, new Callable(this, MethodName.OnPlayerRemovedFromMatchServerSide));
        SignalUtil.ConnectGuarded(TableGame, TableGame.SignalName.PlayerReclaimed, new Callable(this, MethodName.OnPlayerReclaimedServerSide));

        var provider = NetworkManager.Instance?.NetworkProvider;
        if (provider != null)
            provider.PlayerConnected += OnPeerConnected;
    }

    public override void _ExitTree()
    {
        var provider = NetworkManager.Instance?.NetworkProvider;
        if (provider != null)
            provider.PlayerConnected -= OnPeerConnected;
    }

    private void OnPeerConnected(int peerId)
    {
        if (!Multiplayer.IsServer() || TableGame == null || !TableGame.IsMatchActive)
            return;

        // Reliable ordering guarantees this setup reaches the late peer before mode-specific
        // snapshots (the domino board/private hand or the pool table state).
        RpcId(peerId, MethodName.RpcSyncMatchSetup,
            new Array(TableGame.TurnOrder), TableGame.TurnOwnerId,
            TableGame.BuildSeatSlotSnapshot());
    }

    private void OnMatchStartedServerSide(Array playersIds, string firstPlayer)
    {
        if (Multiplayer.IsServer())
            Rpc(MethodName.RpcSyncMatchSetup, playersIds, firstPlayer,
                TableGame.BuildSeatSlotSnapshot());
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

    private void OnPlayerRemovedFromMatchServerSide(string playerId, Array turnOrder)
    {
        if (Multiplayer.IsServer())
            Rpc(MethodName.RpcSyncPlayerRemoved, playerId, turnOrder);
    }

    private void OnPlayerReclaimedServerSide(string oldPlayerId, string newPlayerId, Array turnOrder, Dictionary context)
    {
        if (Multiplayer.IsServer())
            Rpc(MethodName.RpcSyncPlayerReclaimed, oldPlayerId, newPlayerId, turnOrder, context);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcSyncMatchSetup(Array playersIds, string firstPlayer, string[] seatSlots)
    {
        TableGame.StageSeatSlotSnapshot(seatSlots);
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

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcSyncPlayerRemoved(string playerId, Array turnOrder)
    {
        TableGame.ApplyPlayerRemoved(playerId, turnOrder);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcSyncPlayerReclaimed(string oldPlayerId, string newPlayerId, Array turnOrder, Dictionary context)
    {
        TableGame.ApplyPlayerReclaimed(oldPlayerId, newPlayerId, turnOrder, context);
    }
}
