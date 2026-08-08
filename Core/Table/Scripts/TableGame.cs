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
    private Player _turnOwner;
    private string _turnOwnerId = "";

    /// <summary>
    /// The scene node that currently owns the turn. Its id is cached on assignment because a
    /// disconnected Player can be freed before the reconnection grace period expires. Reading
    /// Name from that stale Godot object throws ObjectDisposedException.
    /// </summary>
    public Player TurnOwner
    {
        get => _turnOwner;
        set
        {
            _turnOwner = value;
            if (IsInstanceValid(value))
                _turnOwnerId = (string)value.Name;
            else if (value == null)
                _turnOwnerId = "";
        }
    }

    public string TurnOwnerId
    {
        get
        {
            if (IsInstanceValid(_turnOwner))
                _turnOwnerId = (string)_turnOwner.Name;
            return _turnOwnerId;
        }
    }
    public bool IsMatchActive { get; private set; }
    public Player Player;
    public TableTurnNetworkBridge NetworkTurnSyncronization;

    /// <summary>Game modes can exclude non-competitive matches from persistent rankings.</summary>
    public virtual bool CountsWinsForRanking => true;

    /// <summary>How many players the table advertises room for. Display only.</summary>
    public virtual int MaxPlayers => 4;

    /// <summary>
    /// Whether a match may begin with this many players standing at the table. Pool is happy
    /// alone; modes that need opponents (domino) tighten this.
    /// </summary>
    public virtual bool CanStartWith(int playerCount) => playerCount >= 1;

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
    public delegate void PlayerReclaimedEventHandler(string oldPlayerId, string newPlayerId, Array turnOrder, Dictionary context);

    public virtual void Setup(Table table)
    {
        Table = table;
        TableInfluenceArea = table.TableInfluence;
        SetupNetworkTurnSyncronization(this);
        ConnectSignals();

        NetworkManager.Instance.NetworkProvider.PlayerDisconnected += OnPlayerDisconnected;
    }

    public override void _ExitTree()
    {
        var provider = NetworkManager.Instance?.NetworkProvider;
        if (provider != null)
            provider.PlayerDisconnected -= OnPlayerDisconnected;
    }

    /// <summary>Initializes the mode-independent match identity on every peer.</summary>
    public void PrepareMatch(Array players, string firstTurnOwnerId)
    {
        TurnOrder = new Array(players);
        SetTurnOwner(firstTurnOwnerId);
        if (!IsInstanceValid(Player) || !players.Contains((string)Player.Name))
            Player = null;
        IsMatchActive = true;
    }

    public bool IsTurnOwner(string playerId) =>
        !string.IsNullOrEmpty(playerId) && TurnOwnerId == playerId;

    private void SetTurnOwner(string playerId)
    {
        _turnOwnerId = playerId ?? "";
        _turnOwner = string.IsNullOrEmpty(_turnOwnerId)
            ? null
            : PlayerRegistry.Instance?.GetPlayerById(_turnOwnerId);
    }

    private void OnPlayerDisconnected(int peerId)
    {
        if (!Multiplayer.IsServer() || !IsMatchActive)
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
        if (!Multiplayer.IsServer() || !IsMatchActive)
            return;

        if (!TurnOrder.Contains(playerId))
            return;

        var wasCurrentTurn = IsTurnOwner(playerId);
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
        if (!Multiplayer.IsServer() || !IsMatchActive)
            return;

        var index = TurnOrder.IndexOf(oldPlayerId);
        if (index == -1)
            return;

        var newTurnOrder = new Array(TurnOrder);
        newTurnOrder[index] = newPlayerId;

        var context = GameModeHandler?.CurrentGameMode?.TurnResolver
            ?.BuildHandoffContext(oldPlayerId) ?? new Dictionary();
        ApplyPlayerReclaimed(oldPlayerId, newPlayerId, newTurnOrder, context);
    }

    public void ApplyPlayerReclaimed(string oldPlayerId, string newPlayerId, Array turnOrder, Dictionary context)
    {
        TurnOrder = turnOrder;
        Table.PlayersOnMatch.Remove(oldPlayerId);
        Table.PlayersOnMatch.Add(newPlayerId);

        if (IsTurnOwner(oldPlayerId))
            SetTurnOwner(newPlayerId);

        if (int.TryParse(newPlayerId, out var peerId)
            && peerId == Multiplayer.GetUniqueId())
        {
            Player = PlayerRegistry.Instance?.GetPlayerById(newPlayerId);
        }

        EmitSignal(SignalName.PlayerReclaimed, oldPlayerId, newPlayerId, turnOrder, context);
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
        var currentId = TurnOwnerId;
        if (string.IsNullOrEmpty(currentId) || TurnOrder.Count == 0)
        {
            GD.PushWarning("CallNextTurn: partida sem dono de turno ou ordem de jogadores.");
            return;
        }
        var currentIndex = TurnOrder.IndexOf(currentId);

        if (currentIndex == -1)
        {
            GD.PushWarning($"CallNextTurn: TurnOwner '{currentId}' nao esta em TurnOrder [{string.Join(",", TurnOrder)}]; turno nao avancou.");
            return;
        }

        var nextIndex = (currentIndex + 1) % TurnOrder.Count;
        var nextPlayerId = (string)TurnOrder[nextIndex];

        ApplyNewTurn(nextPlayerId, context);

        GD.Print("Turn passed to: ", nextPlayerId);
    }

    public void ApplyNewTurn(string playerId, Dictionary context)
    {
        var nextPlayer = PlayerRegistry.Instance.GetPlayerById(playerId);
        SetTurnOwner(playerId);

        // A reliable turn packet can arrive just before the corresponding Player node is spawned
        // on a reconnecting peer. The cached identity is enough to apply the rules snapshot; the
        // reclaimed-player packet attaches the concrete node as soon as it exists.
        if (nextPlayer == null)
            GD.PushWarning("Turno recebido antes do jogador ser criado localmente: " + playerId);

        if (GameModeHandler != null)
            GameModeHandler.CurrentGameMode.TurnResolver.HandleNewTurnContext(context);
        else
            GD.PushWarning("Game mode handler is not configured on table: ", Name);

        EmitSignal(SignalName.TurnChanged, playerId, context);
    }

    public void CallExtendCurrentTurn(Dictionary context)
    {
        ApplyTurnExtension(context);
        GD.Print("Turn extended for: ", TurnOwnerId);
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
        if (!IsMatchActive)
            return;

        IsMatchActive = false;
        context ??= new Dictionary();
        context["winner"] = winner;
        GameModeHandler?.CurrentGameMode?.TurnResolver?.HandleMatchEnded();
        if (Multiplayer.IsServer()
            || Table.StateMachine.AuthorityStateSynchronizer == null)
        {
            Table.StateMachine.ChangeState(StatesRef.GameFinished, context);
        }
        EmitSignal(SignalName.MatchOver, winner, context);

        ReleaseMatchControllers();

        if (Multiplayer.IsServer() && winner != null && CountsWinsForRanking)
            MatchRanking.Instance.RegisterWin(winner);
    }

    private void ReleaseMatchControllers()
    {
        foreach (var playerIdVariant in TurnOrder)
        {
            var player = PlayerRegistry.Instance?.GetPlayerById((string)playerIdVariant);
            var handler = player?.GameHandler;
            if (handler == null || !IsInstanceValid(handler.CurrentController)
                || handler.CurrentTableGame != this)
                continue;

            handler.CurrentController.GiveControl();
            handler.UnequipCurrentController();
            player.TakeControl();
        }
    }
}
