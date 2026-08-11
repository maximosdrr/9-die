using Godot;
using Godot.Collections;

[GlobalClass]
public partial class TableGame : Node3D
{
    internal const int SurrenderRequestsPerSecond = 2;
    internal const int MaximumTrackedSurrenderPeers = 16;

    public Table Table;
    [ExportGroup("Scene References")]
    [Export] public GameModeHandler GameModeHandler;
    public Array TurnOrder = new();
    private readonly System.Collections.Generic.Dictionary<string, int> _seatAssignments =
        new(System.StringComparer.Ordinal);
    private string[] _stagedSeatSlots;
    private Player _turnOwner;
    private string _turnOwnerId = "";
    private ulong _statePublicationRevision;
    private readonly PeerRequestRateLimiter _surrenderRequestLimiter = new(
        SurrenderRequestsPerSecond,
        windowMilliseconds: 1_000,
        maxTrackedPeers: MaximumTrackedSurrenderPeers);

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
    public TableTurnNetworkBridge NetworkTurnSynchronization;

    /// <summary>Game modes can exclude non-competitive matches from persistent rankings.</summary>
    public virtual bool CountsWinsForRanking => true;

    /// <summary>The fewest players required for an authoritative match start.</summary>
    public virtual int MinimumPlayers => 1;

    /// <summary>The most players accepted by the rules and presentation of this game.</summary>
    public virtual int MaximumPlayers => 4;

    /// <summary>Compatibility read for existing UI and callers. New code should use MaximumPlayers.</summary>
    public int MaxPlayers => MaximumPlayers;

    /// <summary>
    /// Whether a match may begin with this many players standing at the table. The shared
    /// implementation enforces both capacity boundaries so a game cannot accidentally advertise
    /// one limit while accepting another.
    /// </summary>
    public bool CanStartWith(int playerCount) =>
        playerCount >= MinimumPlayers && playerCount <= MaximumPlayers;

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
        SetupNetworkTurnSynchronization(this);

        NetworkManager.Instance.NetworkProvider.PlayerDisconnected += OnPlayerDisconnected;
    }

    public override void _ExitTree()
    {
        _surrenderRequestLimiter.Clear();

        var provider = NetworkManager.Instance?.NetworkProvider;
        if (provider != null)
            provider.PlayerDisconnected -= OnPlayerDisconnected;
    }

    /// <summary>Initializes the mode-independent match identity on every peer.</summary>
    public void PrepareMatch(Array players, string firstTurnOwnerId)
    {
        _surrenderRequestLimiter.Clear();
        TurnOrder = new Array(players);
        InitializeSeatAssignments(players);
        SetTurnOwner(firstTurnOwnerId);
        if (!IsInstanceValid(Player) || !players.Contains((string)Player.Name))
            Player = null;
        IsMatchActive = true;
    }

    /// <summary>
    /// Returns the physical seat reserved for a player for the lifetime of this match. TurnOrder
    /// is deliberately compacted when somebody leaves because it drives turn rotation; using its
    /// current index for presentation would silently move every player after the empty slot to a
    /// different chair.
    /// </summary>
    public int SeatIndexFor(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
            return -1;

        if (_seatAssignments.TryGetValue(playerId, out var seatIndex))
            return seatIndex;

        // A few editor/test fixtures still populate TurnOrder directly. Preserve that harmless
        // pre-match behavior while authoritative matches always use the stable map above.
        return _seatAssignments.Count == 0 ? TurnOrder.IndexOf(playerId) : -1;
    }

    /// <summary>Returns the current occupant of a physical seat, or an empty id for a vacant seat.</summary>
    public string PlayerIdAtSeat(int seatIndex)
    {
        if (seatIndex < 0)
            return "";

        foreach (var assignment in _seatAssignments)
        {
            if (assignment.Value == seatIndex && TurnOrder.Contains(assignment.Key))
                return assignment.Key;
        }

        if (_seatAssignments.Count == 0 && seatIndex < TurnOrder.Count)
            return (string)TurnOrder[seatIndex];

        return "";
    }

    /// <summary>
    /// Serializes physical seat reservations, including former occupants. TurnOrder remains the
    /// source of truth for occupancy; retaining the old id here keeps authored anchors available
    /// for teardown and in-hand presentation after a middle player leaves.
    /// </summary>
    public string[] BuildSeatSlotSnapshot()
    {
        var slots = new string[MaximumPlayers];
        System.Array.Fill(slots, "");
        foreach (var assignment in _seatAssignments)
        {
            if (assignment.Value >= 0 && assignment.Value < slots.Length)
                slots[assignment.Value] = assignment.Key;
        }

        return slots;
    }

    /// <summary>
    /// Stages the authoritative physical layout for the next SetupMatch. Derived modes call
    /// PrepareMatch before equipping controllers, so consuming it there prevents a late peer from
    /// ever rendering a compacted, incorrect chair assignment for one frame.
    /// </summary>
    internal void StageSeatSlotSnapshot(string[] seatSlots)
    {
        _stagedSeatSlots = seatSlots == null ? null : (string[])seatSlots.Clone();
    }

    private void InitializeSeatAssignments(Array players)
    {
        _seatAssignments.Clear();

        var stagedSeatSlots = _stagedSeatSlots;
        _stagedSeatSlots = null;
        if (stagedSeatSlots != null)
        {
            if (TryRestoreSeatAssignments(players, stagedSeatSlots))
                return;

            GD.PushWarning("Snapshot de assentos invalido; usando a ordem compacta da partida.");
            _seatAssignments.Clear();
        }

        for (var seatIndex = 0; seatIndex < players.Count; seatIndex++)
        {
            var playerId = (string)players[seatIndex];
            if (!string.IsNullOrEmpty(playerId) && !_seatAssignments.ContainsKey(playerId))
                _seatAssignments[playerId] = seatIndex;
        }
    }

    private bool TryRestoreSeatAssignments(Array players, string[] seatSlots)
    {
        if (seatSlots.Length != MaximumPlayers)
            return false;

        var expectedPlayers = new System.Collections.Generic.HashSet<string>(
            System.StringComparer.Ordinal);
        foreach (var playerVariant in players)
        {
            var playerId = (string)playerVariant;
            if (string.IsNullOrEmpty(playerId) || !expectedPlayers.Add(playerId))
                return false;
        }

        var restored = new System.Collections.Generic.Dictionary<string, int>(
            System.StringComparer.Ordinal);
        var seenSlotIds = new System.Collections.Generic.HashSet<string>(
            System.StringComparer.Ordinal);
        for (var seatIndex = 0; seatIndex < seatSlots.Length; seatIndex++)
        {
            var playerId = seatSlots[seatIndex] ?? "";
            if (string.IsNullOrEmpty(playerId))
                continue;

            if (!seenSlotIds.Add(playerId))
                return false;

            restored[playerId] = seatIndex;
            expectedPlayers.Remove(playerId);
        }

        if (expectedPlayers.Count != 0)
            return false;

        foreach (var assignment in restored)
            _seatAssignments[assignment.Key] = assignment.Value;
        return true;
    }

    public bool IsTurnOwner(string playerId) =>
        !string.IsNullOrEmpty(playerId) && TurnOwnerId == playerId;

    private void SetTurnOwner(string playerId)
    {
        _turnOwnerId = playerId ?? "";
        _turnOwner = string.IsNullOrEmpty(_turnOwnerId)
            ? null
            : FindRegisteredPlayer(_turnOwnerId);
    }

    private static Player FindRegisteredPlayer(string playerId)
    {
        var registry = PlayerRegistry.Instance;
        return registry != null && registry.TryGetPlayerById(playerId, out var player)
            ? player
            : null;
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

        var requesterId = Multiplayer.GetRemoteSenderId();
        if (!_surrenderRequestLimiter.TryConsume(requesterId))
            return;

        ProcessSurrender(requesterId, playerId);
    }

    private void ProcessSurrender(int requesterId, string playerId)
    {
        if (!IsSurrenderIdentityValid(requesterId, playerId))
            return;

        RemovePlayerFromMatch(playerId, "opponent_left");
    }

    internal static bool IsSurrenderIdentityValid(int requesterId, string playerId) =>
        requesterId > 0 && requesterId.ToString() == playerId;

    internal bool TryConsumeSurrenderRequest(int peerId, ulong nowMilliseconds) =>
        _surrenderRequestLimiter.TryConsume(peerId, nowMilliseconds);

    internal int TrackedSurrenderPeerCount => _surrenderRequestLimiter.TrackedPeerCount;

    public void RemovePlayerFromMatch(string playerId, string reason)
    {
        if (!Multiplayer.IsServer() || !IsMatchActive)
            return;

        if (!TurnOrder.Contains(playerId))
            return;

        var wasCurrentTurn = IsTurnOwner(playerId);
        var previousIndex = TurnOrder.IndexOf(playerId);
        var publicationRevision = _statePublicationRevision;

        var newTurnOrder = new Array(TurnOrder);
        newTurnOrder.Remove(playerId);

        ApplyPlayerRemoved(playerId, newTurnOrder);

        // Mode-specific removal handlers run synchronously from PlayerRemovedFromMatch. Poker can
        // finish or advance the hand there; if it already published authoritative state, a generic
        // fallback here would send a second, contradictory turn packet.
        if (!IsMatchActive || _statePublicationRevision != publicationRevision)
            return;

        if (TurnOrder.Count <= 1)
        {
            var winnerId = TurnOrder.Count == 1 ? (string)TurnOrder[0] : null;
            ApplyMatchOver(winnerId, new Dictionary { ["reason"] = reason });
            return;
        }

        // PlayerRemovedFromMatch lets the mode mutate private state first (for example Domino
        // returns the leaver's hand to the boneyard). Publish a fresh context afterwards even when
        // the current turn did not change, otherwise remote peers keep a stale rules snapshot.
        var handoffContext = GameModeHandler?.CurrentGameMode?.TurnResolver
            ?.BuildHandoffContext(playerId) ?? new Dictionary();

        if (wasCurrentTurn)
        {
            var nextIndex = previousIndex % TurnOrder.Count;
            var nextPlayerId = (string)TurnOrder[nextIndex];
            ApplyNewTurn(nextPlayerId, handoffContext);
        }
        else
        {
            ApplyTurnExtension(handoffContext);
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
            ?.BuildReclaimContext(oldPlayerId) ?? new Dictionary();
        ApplyPlayerReclaimed(oldPlayerId, newPlayerId, newTurnOrder, context);
    }

    public void ApplyPlayerReclaimed(string oldPlayerId, string newPlayerId, Array turnOrder, Dictionary context)
    {
        var reclaimedSeat = SeatIndexFor(oldPlayerId);
        _seatAssignments.Remove(oldPlayerId);
        if (reclaimedSeat >= 0 && !string.IsNullOrEmpty(newPlayerId))
            _seatAssignments[newPlayerId] = reclaimedSeat;

        TurnOrder = turnOrder;
        Table.PlayersOnMatch.Remove(oldPlayerId);
        Table.PlayersOnMatch.Add(newPlayerId);

        if (IsTurnOwner(oldPlayerId))
            SetTurnOwner(newPlayerId);

        var reclaimedPlayer = FindRegisteredPlayer(newPlayerId);
        if (reclaimedPlayer != null)
            AttachLocalReclaimedPlayer(newPlayerId, reclaimedPlayer);

        EmitSignal(SignalName.PlayerReclaimed, oldPlayerId, newPlayerId, turnOrder, context);
    }

    /// <summary>
    /// Reclaim replication can precede Player spawning. Mode-specific retry loops call this when
    /// the node finally appears so local-only UI and action routing regain their concrete owner.
    /// </summary>
    internal bool AttachLocalReclaimedPlayer(string playerId, Player player) =>
        AttachLocalReclaimedPlayer(playerId, player, Multiplayer.GetUniqueId());

    internal bool AttachLocalReclaimedPlayer(string playerId, Player player, int localPeerId)
    {
        if (!IsInstanceValid(player)
            || (string)player.Name != playerId
            || !int.TryParse(playerId, out var playerPeerId)
            || playerPeerId != localPeerId)
        {
            return false;
        }

        Player = player;
        return true;
    }

    private void SetupNetworkTurnSynchronization(TableGame tableGame)
    {
        if (!Table.EnableNetworkTurnSynchronization)
            return;

        NetworkTurnSynchronization = new TableTurnNetworkBridge();
        NetworkTurnSynchronization.Name = "TableTurnNetworkBridge";

        AddChild(NetworkTurnSynchronization);

        NetworkTurnSynchronization.Setup(tableGame);
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
        _statePublicationRevision++;
        var nextPlayer = FindRegisteredPlayer(playerId);
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
        _statePublicationRevision++;
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

        _statePublicationRevision++;
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
            var player = FindRegisteredPlayer((string)playerIdVariant);
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
