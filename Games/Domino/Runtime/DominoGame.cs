using System.Collections.Generic;
using Domino.Rules;
using Godot;
using Godot.Collections;

/// <summary>
/// Double-six dominoes for two to four players, each for themselves, one round per match.
///
/// Holds only the PUBLIC state — the chain, the open ends, how many tiles everyone is holding and
/// how many are left in the boneyard — plus this peer's own hand. Nobody else's tiles are ever
/// stored here, because everything here reaches every peer. The secret half lives on
/// <see cref="DominoTurnResolver"/>, server side, and never leaves it.
/// </summary>
[GlobalClass]
public partial class DominoGame : TableGame
{
    [Export] public PackedScene GameControllerScene;
    [Export] public DominoChainPresenter ChainPresenter;
    [Export] public Node3D Seats;

    /// <summary>
    /// Lets a lone host start a match to try the game out. Solo matches are excluded from the
    /// ranking, the same way a solo pool frame is.
    /// </summary>
    [Export] public bool AllowSoloDebug;

    /// <summary>
    /// Temporary presentation aid. Zero keeps the official deal; a positive value is used only when
    /// every seated player can receive that many tiles from the double-six set.
    /// </summary>
    [Export(PropertyHint.Range, "0,14,1")] public int DebugStartingHandSize;

    public GlobalCamera Camera;
    public bool IsSoloMatch { get; private set; }

    public readonly List<PlayRecord> Plays = new();

    /// <summary>How many tiles each player is holding. Counts only — never contents.</summary>
    public readonly System.Collections.Generic.Dictionary<string, int> HandCounts = new();

    public int LeftEnd = DominoTileId.NoEnd;
    public int RightEnd = DominoTileId.NoEnd;
    /// <summary>
    /// The places on the table still holding a stock tile. Places, never tiles — what is face down
    /// stays face down. The hand view aims at these; the server maps a place back to a tile.
    /// </summary>
    public int[] BoneyardSlots = System.Array.Empty<int>();

    public int BoneyardCount => BoneyardSlots.Length;

    /// <summary>
    /// Where the stock lies on the cloth. Kept here, on the shared game, because the seat presenter
    /// draws the face-down tiles from it and the hand view aims at them with it — two readers of
    /// one value, so the crosshair can never point at where the tiles are not.
    /// </summary>
    public SlotGridSpec StockSpec { get; set; } = DominoBoneyardLayout.Default;

    /// <summary>Server-issued stamp for the current turn; a request carrying a stale one is dropped.</summary>
    public int TurnToken;

    public string LastAction = "";
    public string LastPlayer = "";
    public int LastTile = DominoTileId.NoEnd;

    /// <summary>This peer's own tiles. Empty on every peer that is not their owner.</summary>
    public int[] LocalHand = System.Array.Empty<int>();

    private GameModeHandler _gameModeHandler;
    private readonly HashSet<string> _pendingReclaimedControllerPlayerIds =
        new(System.StringComparer.Ordinal);

    public override int MinimumPlayers => AllowSoloDebug ? 1 : 2;
    public override int MaximumPlayers => 4;

    public override bool CountsWinsForRanking => !IsSoloMatch;

    [Signal]
    public delegate void HudStateUpdatedEventHandler();

    [Signal]
    public delegate void LocalHandChangedEventHandler();

    public DominoTurnResolver Resolver =>
        _gameModeHandler?.CurrentGameMode?.TurnResolver as DominoTurnResolver;

    public override void _Ready()
    {
        SetProcess(false);
        _gameModeHandler = GameModeHandler;

        MatchStarted += OnMatchStarts;
        MatchOver += OnMatchIsOver;
        PlayerRemovedFromMatch += OnPlayerRemovedFromMatch;
        PlayerReclaimed += OnPlayerReclaimed;
    }

    public override void _Process(double delta)
    {
        if (_pendingReclaimedControllerPlayerIds.Count == 0)
        {
            SetProcess(false);
            return;
        }

        if (!IsMatchActive)
        {
            ClearPendingReclaimedControllers();
            return;
        }

        // Several remote peers can reconnect during the same spawn window. Keep every request and
        // consume each id independently; a single string here used to let the last packet silently
        // overwrite all earlier reconnects.
        foreach (var playerId in new List<string>(_pendingReclaimedControllerPlayerIds))
        {
            if (!TurnOrder.Contains(playerId))
            {
                _pendingReclaimedControllerPlayerIds.Remove(playerId);
                continue;
            }

            if (TryEquipReclaimedController(playerId))
                _pendingReclaimedControllerPlayerIds.Remove(playerId);
        }

        SetProcess(_pendingReclaimedControllerPlayerIds.Count > 0);
    }

    public override void _ExitTree()
    {
        ClearPendingReclaimedControllers();
        base._ExitTree();
    }

    public override void SetCamera(GlobalCamera camera)
    {
        Camera = camera;
    }

    public Marker3D SeatFor(string playerId)
    {
        var index = SeatIndexFor(playerId);
        if (index < 0 || Seats == null || index >= Seats.GetChildCount())
            return null;

        return Seats.GetChild(index) as Marker3D;
    }

    public override void SetupMatch(Array players, string firstTurnOwner)
    {
        ClearPendingReclaimedControllers();
        PrepareMatch(players, firstTurnOwner);
        IsSoloMatch = players.Count == 1;

        ResetPublicState();
        ChainPresenter?.Clear();

        _gameModeHandler.Setup(this);

        // Clients equip their controllers and take their seats off this signal, so it has to reach
        // them before the deal does. The turn bridge mirrors it on the same reliable channel the
        // deal uses, which keeps that order.
        EmitSignal(SignalName.MatchStarted, players, firstTurnOwner);

        if (Multiplayer.IsServer())
            Resolver?.BeginDeal(players);
    }

    private void ResetPublicState()
    {
        Plays.Clear();
        HandCounts.Clear();
        LeftEnd = DominoTileId.NoEnd;
        RightEnd = DominoTileId.NoEnd;
        BoneyardSlots = System.Array.Empty<int>();
        TurnToken = 0;
        LastAction = "";
        LastPlayer = "";
        LastTile = DominoTileId.NoEnd;
        LocalHand = System.Array.Empty<int>();
    }

    /// <summary>
    /// Takes the public snapshot the server packed into a turn context. Every state change already
    /// travels as a turn context, so this is the single place the table catches up — there is no
    /// second sync channel that could arrive out of order with the turn itself.
    /// </summary>
    public void ApplyPublicSnapshot(Dictionary context)
    {
        if (context == null || !context.ContainsKey("play_tiles"))
            return;

        var tiles = context["play_tiles"].AsInt32Array();
        var ends = context["play_ends"].AsInt32Array();
        var players = context["play_players"].AsStringArray();

        Plays.Clear();
        for (var i = 0; i < tiles.Length; i++)
        {
            var playerId = i < players.Length ? players[i] : "";
            var end = i < ends.Length && ends[i] == (int)ChainEnd.Left ? ChainEnd.Left : ChainEnd.Right;
            Plays.Add(new PlayRecord(playerId, tiles[i], end));
        }

        LeftEnd = (int)context["left_end"];
        RightEnd = (int)context["right_end"];
        BoneyardSlots = context["boneyard_slots"].AsInt32Array();
        TurnToken = (int)context["turn_token"];

        HandCounts.Clear();
        var handPlayers = context["hand_players"].AsStringArray();
        var handSizes = context["hand_counts"].AsInt32Array();
        for (var i = 0; i < handPlayers.Length && i < handSizes.Length; i++)
            HandCounts[handPlayers[i]] = handSizes[i];

        LastAction = context.TryGetValue("last_action", out var action) ? (string)action : "";
        LastPlayer = context.TryGetValue("last_player", out var player) ? (string)player : "";
        LastTile = context.TryGetValue("last_tile", out var tile) ? (int)tile : DominoTileId.NoEnd;

        ChainPresenter?.Sync(Plays);
        EmitSignal(SignalName.HudStateUpdated);
    }

    /// <summary>Called only on the peer the hand belongs to, from a targeted RPC.</summary>
    public void ApplyLocalHand(int[] tiles)
    {
        LocalHand = tiles ?? System.Array.Empty<int>();
        EmitSignal(SignalName.LocalHandChanged);
        EmitSignal(SignalName.HudStateUpdated);
    }

    private void OnMatchStarts(Array playersIds, string firstTurnOwner)
    {
        foreach (var playerIdVariant in playersIds)
        {
            var player = PlayerRegistry.Instance.GetPlayerById((string)playerIdVariant);
            player?.GameHandler.EquipGameController(GameControllerScene, this, Camera);
        }
    }

    private void OnMatchIsOver(string winner, Dictionary context)
    {
        ClearPendingReclaimedControllers();

        // The winning tile reaches the table through this context rather than a turn change,
        // because a match-ending play never hands the turn on.
        ApplyPublicSnapshot(context);

        if (Player != null && Player.GameHandler.CurrentController != null)
        {
            Player.GameHandler.CurrentController.GiveControl();
            Player.GameHandler.UnequipCurrentController();
            Player.TakeControl();
        }
    }

    private void OnPlayerRemovedFromMatch(string playerId, Array turnOrder)
    {
        HandCounts.Remove(playerId);
        _pendingReclaimedControllerPlayerIds.Remove(playerId);
        SetProcess(_pendingReclaimedControllerPlayerIds.Count > 0);

        // TableGame fires this signal before it builds the handoff context, so moving the real
        // tiles now is what keeps the immediately published snapshot's counts honest.
        if (Multiplayer.IsServer())
            Resolver?.ReturnTilesToBoneyard(playerId);

        EmitSignal(SignalName.HudStateUpdated);

        var registry = PlayerRegistry.Instance;
        if (registry == null || !registry.TryGetPlayerById(playerId, out var leavingPlayer)
            || leavingPlayer?.GameHandler.CurrentController == null)
            return;

        leavingPlayer.GameHandler.CurrentController.GiveControl();
        leavingPlayer.GameHandler.UnequipCurrentController();
        leavingPlayer.TakeControl();
    }

    private void OnPlayerReclaimed(string oldPlayerId, string newPlayerId, Array turnOrder, Dictionary context)
    {
        MigrateReclaimedPlayerState(oldPlayerId, newPlayerId);

        // The snapshot was captured immediately before TableGame replaced the peer id. Apply a
        // re-keyed copy on every peer so a current-player reconnect publishes its new turn stamp,
        // while reconnecting another seat leaves the actor's existing token untouched.
        var reclaimedContext = RemapReclaimedContext(context, oldPlayerId, newPlayerId);
        if (reclaimedContext.ContainsKey("play_tiles")
            && reclaimedContext.ContainsKey("turn_token"))
        {
            ApplyPublicSnapshot(reclaimedContext);
        }
        else
        {
            EmitSignal(SignalName.HudStateUpdated);
        }

        // The reliable reclaim packet can arrive just before the replacement Player node is
        // spawned. Public and secret match identity migrate immediately; presentation waits for
        // the registry to observe that node instead of being lost permanently.
        if (TryEquipReclaimedController(newPlayerId))
        {
            _pendingReclaimedControllerPlayerIds.Remove(newPlayerId);
            SetProcess(_pendingReclaimedControllerPlayerIds.Count > 0);
            return;
        }

        if (GameControllerScene != null && IsMatchActive && turnOrder.Contains(newPlayerId))
        {
            _pendingReclaimedControllerPlayerIds.Add(newPlayerId);
            SetProcess(true);
        }
    }

    private bool TryEquipReclaimedController(string playerId)
    {
        var registry = PlayerRegistry.Instance;
        if (registry == null || !registry.TryGetPlayerById(playerId, out var player)
            || !IsInstanceValid(player))
            return false;

        AttachLocalReclaimedPlayer(playerId, player);
        if (GameControllerScene == null || player.GameHandler == null)
            return false;

        if (player.GameHandler.CurrentTableGame != this
            || !IsInstanceValid(player.GameHandler.CurrentController))
        {
            player.GameHandler.EquipGameController(GameControllerScene, this, Camera);
        }
        return true;
    }

    internal void MigrateReclaimedPlayerState(string oldPlayerId, string newPlayerId)
    {
        if (HandCounts.TryGetValue(oldPlayerId, out var heldTiles))
        {
            HandCounts.Remove(oldPlayerId);
            HandCounts[newPlayerId] = heldTiles;
        }

        if (Multiplayer.IsServer())
            Resolver?.ReissueStateTo(oldPlayerId, newPlayerId);
    }

    internal static Dictionary RemapReclaimedContext(
        Dictionary context, string oldPlayerId, string newPlayerId)
    {
        var remapped = context?.Duplicate() ?? new Dictionary();
        if (string.IsNullOrEmpty(oldPlayerId) || string.IsNullOrEmpty(newPlayerId)
            || oldPlayerId == newPlayerId)
        {
            return remapped;
        }

        foreach (Variant key in remapped.Keys)
        {
            var value = remapped[key];
            if (value.VariantType == Variant.Type.String)
            {
                if (value.AsString() == oldPlayerId)
                    remapped[key] = newPlayerId;
                continue;
            }

            if (value.VariantType != Variant.Type.PackedStringArray)
                continue;

            var playerIds = value.AsStringArray();
            var changed = false;
            for (var index = 0; index < playerIds.Length; index++)
            {
                if (playerIds[index] != oldPlayerId)
                    continue;

                playerIds[index] = newPlayerId;
                changed = true;
            }

            if (changed)
                remapped[key] = playerIds;
        }

        return remapped;
    }

    internal bool HasPendingReclaimedController =>
        _pendingReclaimedControllerPlayerIds.Count > 0;

    internal int PendingReclaimedControllerCount =>
        _pendingReclaimedControllerPlayerIds.Count;

    internal bool IsReclaimedControllerPending(string playerId) =>
        _pendingReclaimedControllerPlayerIds.Contains(playerId);

    private void ClearPendingReclaimedControllers()
    {
        _pendingReclaimedControllerPlayerIds.Clear();
        SetProcess(false);
    }
}
