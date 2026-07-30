using Godot;
using Godot.Collections;

[GlobalClass]
public partial class TableGame : Node3D
{
    public Dictionary<ulong, Player> PlayersOnArea = new();
    public Area3D TableInfluenceArea;
    public PlayersContainer PlayersContainer;
    public Table Table;
    public GameModeHandler GameModeHandler;
    public Array TurnOrder = new();
    public Player TurnOwner;
    public Player Player;
    public TableTurnNetworkBridge NetworkTurnSyncronization;

    [Signal]
    public delegate void TurnChangedEventHandler(string nextPlayerName, Dictionary context);

    [Signal]
    public delegate void MatchStartedEventHandler(Array playersIds, string firstTurnPlayer);

    [Signal]
    public delegate void TurnExtendedEventHandler(Dictionary context);

    [Signal]
    public delegate void MatchOverEventHandler(string winner, Dictionary context);

    [Signal]
    public delegate void PlayerRemovedFromMatchEventHandler(string playerId, Array turnOrder);

    [Signal]
    public delegate void PlayerReclaimedEventHandler(string oldPlayerId, string newPlayerId, Array turnOrder);

    public virtual void Setup(Table table)
    {
        Table = table;
        TableInfluenceArea = table.TableInfluence;
        SetupNetworkTurnSyncronization(this);
        ConnectSignals();

        NetworkManager.Instance.NetworkProvider.PlayerDisconnected += OnPlayerDisconnected;
    }

    private void OnPlayerDisconnected(int peerId)
    {
        if (!Multiplayer.IsServer())
            return;

        var playerId = peerId.ToString();
        if (!TurnOrder.Contains(playerId))
            return;

        var token = ReconnectionManager.Instance.GetToken(peerId);
        if (string.IsNullOrEmpty(token))
        {
            RemovePlayerFromMatch(playerId, "opponent_disconnected");
            return;
        }

        ReconnectionManager.Instance.BeginGracePeriod(token, playerId, this);
    }

    public void RequestSurrender(string playerId)
    {
        if (Multiplayer.IsServer())
            ProcessSurrender(Multiplayer.GetUniqueId(), playerId);
        else
            RpcId(1, MethodName.RequestSurrenderOnServer, playerId);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestSurrenderOnServer(string playerId)
    {
        if (!Multiplayer.IsServer())
            return;

        ProcessSurrender(Multiplayer.GetRemoteSenderId(), playerId);
    }

    private void ProcessSurrender(int requesterId, string playerId)
    {
        if (requesterId.ToString() != playerId)
        {
            GD.PushWarning($"Pedido de desistência rejeitado: peer {requesterId} tentou desistir por {playerId}.");
            return;
        }

        RemovePlayerFromMatch(playerId, "opponent_left");
    }

    public void RemovePlayerFromMatch(string playerId, string reason)
    {
        if (!Multiplayer.IsServer())
            return;

        if (!TurnOrder.Contains(playerId))
            return;

        var wasCurrentTurn = TurnOwner != null && (string)TurnOwner.Name == playerId;
        var previousIndex = TurnOrder.IndexOf(playerId);

        var newTurnOrder = new Array(TurnOrder);
        newTurnOrder.Remove(playerId);

        ApplyPlayerRemoved(playerId, newTurnOrder);

        if (TurnOrder.Count <= 1)
        {
            var winnerId = TurnOrder.Count == 1 ? (string)TurnOrder[0] : null;
            ApplyMatchOver(winnerId, new Dictionary { ["reason"] = reason });
            return;
        }

        if (wasCurrentTurn)
        {
            var nextIndex = previousIndex % TurnOrder.Count;
            var nextPlayerId = (string)TurnOrder[nextIndex];
            var handoffContext = GameModeHandler != null
                ? GameModeHandler.CurrentGameMode.TurnResolver.BuildHandoffContext(playerId)
                : new Dictionary();
            ApplyNewTurn(nextPlayerId, handoffContext);
        }
    }

    public void ApplyPlayerRemoved(string playerId, Array turnOrder)
    {
        TurnOrder = turnOrder;
        Table.PlayersOnMatch.Remove(playerId);

        EmitSignal(SignalName.PlayerRemovedFromMatch, playerId, turnOrder);
    }

    // Called by ReconnectionManager once a reconnecting peer's token matches a
    // slot that's still within its grace period — hands the match state back to
    // the newly spawned Player node for that peer instead of forfeiting.
    public void ReclaimSlot(string oldPlayerId, string newPlayerId)
    {
        if (!Multiplayer.IsServer())
            return;

        var index = TurnOrder.IndexOf(oldPlayerId);
        if (index == -1)
            return;

        var newTurnOrder = new Array(TurnOrder);
        newTurnOrder[index] = newPlayerId;

        ApplyPlayerReclaimed(oldPlayerId, newPlayerId, newTurnOrder);
    }

    public void ApplyPlayerReclaimed(string oldPlayerId, string newPlayerId, Array turnOrder)
    {
        TurnOrder = turnOrder;
        Table.PlayersOnMatch.Remove(oldPlayerId);
        Table.PlayersOnMatch.Add(newPlayerId);

        EmitSignal(SignalName.PlayerReclaimed, oldPlayerId, newPlayerId, turnOrder);
    }

    private void SetupNetworkTurnSyncronization(TableGame tableGame)
    {
        if (!Table.EnableNetworkTurnSyncronization)
            return;

        NetworkTurnSyncronization = new TableTurnNetworkBridge();
        NetworkTurnSyncronization.Name = "TableTurnNetworkBridge";

        AddChild(NetworkTurnSyncronization);

        NetworkTurnSyncronization.Setup(tableGame);
    }

    private void ConnectSignals()
    {
        SignalUtil.ConnectGuarded(TableInfluenceArea, Area3D.SignalName.BodyEntered, new Callable(this, MethodName.OnTableInfluenceBodyEntered));
        SignalUtil.ConnectGuarded(TableInfluenceArea, Area3D.SignalName.BodyExited, new Callable(this, MethodName.OnTableInfluenceBodyExited));
    }

    private void OnTableInfluenceBodyEntered(Node3D body)
    {
        if (body is not Player player)
            return;

        var id = player.GetInstanceId();
        if (PlayersOnArea.ContainsKey(id))
            return;

        PlayersOnArea[id] = player;
    }

    private void OnTableInfluenceBodyExited(Node3D body)
    {
        if (body is not Player player)
            return;

        PlayersOnArea.Remove(player.GetInstanceId());
    }

    public virtual void SetupMatch(Array players, string firstTurnOwnerId) { }

    public virtual void SetCamera(GlobalCamera camera) { }

    public void CallNextTurn(Dictionary context)
    {
        var currentId = (string)TurnOwner.Name;
        var currentIndex = TurnOrder.IndexOf(currentId);

        if (currentIndex == -1)
            return;

        var nextIndex = (currentIndex + 1) % TurnOrder.Count;
        var nextPlayerId = (string)TurnOrder[nextIndex];

        ApplyNewTurn(nextPlayerId, context);

        GD.Print("Turn passed to: ", nextPlayerId);
    }

    public void ApplyNewTurn(string playerId, Dictionary context)
    {
        var nextPlayer = PlayerRegistry.Instance.GetPlayerById(playerId);

        if (nextPlayer == null)
        {
            GD.PushError("Tentativa de mudar turno para jogador inexistente: " + playerId);
            return;
        }

        TurnOwner = nextPlayer;

        if (GameModeHandler != null)
            GameModeHandler.CurrentGameMode.TurnResolver.HandleNewTurnContext(context);
        else
            GD.PushWarning("Game mode handler is not configured on table: ", Name);

        EmitSignal(SignalName.TurnChanged, playerId, context);
    }

    public void CallExtendCurrentTurn(Dictionary context)
    {
        ApplyTurnExtension(context);
        GD.Print("Turn extended for: ", TurnOwner.Name);
    }

    public void ApplyTurnExtension(Dictionary context)
    {
        EmitSignal(SignalName.TurnExtended, context);

        if (GameModeHandler != null)
            GameModeHandler.CurrentGameMode.TurnResolver.HandleTurnExtensionContext(context);
        else
            GD.PushWarning("Game mode handler is not configured on table: ", Name);
    }

    public void ApplyMatchOver(string winner, Dictionary context)
    {
        context["winner"] = winner;
        Table.StateMachine.ChangeState(StatesRef.GameFinished, context);
        EmitSignal(SignalName.MatchOver, winner, context);

        if (Multiplayer.IsServer() && winner != null)
            MatchRanking.Instance.RegisterWin(winner);
    }
}
