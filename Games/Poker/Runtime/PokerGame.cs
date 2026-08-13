using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;
using Poker.Rules;

/// <summary>
/// A server-approved, reversible wager that is visible on the cloth but has not changed any poker
/// balance yet. The ordered denominations are presentation identity: every peer can reproduce the
/// same sequence of selected chips, while the authoritative ledger remains in PokerTurnResolver.
/// </summary>
public sealed class PokerPreparedWagerSnapshot
{
    private readonly List<int> _denominations;

    public string PlayerId { get; }
    public int TurnToken { get; }
    public int Revision { get; }
    public IReadOnlyList<int> Denominations => _denominations;
    public int CommittedActionSeq { get; }
    public bool IsCommitted => CommittedActionSeq >= 0;

    public int Amount
    {
        get
        {
            var amount = 0;
            foreach (var denomination in _denominations)
                amount += denomination;
            return amount;
        }
    }

    public PokerPreparedWagerSnapshot(
        string playerId, int turnToken, int revision,
        IEnumerable<int> denominations, int committedActionSeq = -1)
    {
        PlayerId = playerId ?? "";
        TurnToken = turnToken;
        Revision = revision;
        _denominations = denominations == null
            ? new List<int>() : new List<int>(denominations);
        CommittedActionSeq = committedActionSeq;
    }

    public bool SameAs(PokerPreparedWagerSnapshot other)
    {
        if (other == null || PlayerId != other.PlayerId || TurnToken != other.TurnToken
            || Revision != other.Revision || CommittedActionSeq != other.CommittedActionSeq
            || _denominations.Count != other._denominations.Count)
            return false;

        for (var index = 0; index < _denominations.Count; index++)
        {
            if (_denominations[index] != other._denominations[index])
                return false;
        }
        return true;
    }
}

/// <summary>
/// Texas Hold'em for two to four players, played as a SESSION: hands run one after another with the
/// button going round, and the match ends when one player holds every chip.
///
/// Holds only the PUBLIC state — the board as far as it has been turned, everyone's stack and bet,
/// who has folded, whose turn it is — plus this peer's own two cards. Nobody else's hole cards are
/// ever stored here, because everything here reaches every peer. The secret half lives on
/// <see cref="PokerTurnResolver"/>, server side, and never leaves it until a showdown forces it.
/// </summary>
[GlobalClass]
public partial class PokerGame : TableGame
{
    [Export] public PackedScene GameControllerScene;
    [Export] public Node3D Seats;

    [ExportGroup("Replaceable presentation assets")]
    /// <summary>
    /// One resource owns every swappable poker visual. Motion and rules only depend on the stable
    /// PokerCard/PokerChipVisual adapters, never on an imported mesh hierarchy.
    /// </summary>
    [Export] public PokerVisualAssets VisualAssets;

    /// <summary>
    /// The cloth: it draws the community cards and the pot, and it doubles as the surface the
    /// overhead camera centres on and the crosshair aims at.
    /// </summary>
    [Export] public PokerBoardPresenter BoardPresenter;

    /// <summary>
    /// Draws what each seat owns. The hand view waits on it to know the deal has landed before
    /// playing the pick-up.
    /// </summary>
    [Export] public PokerSeatPresenter SeatPresenter;

    /// <summary>Editor-authored physical layout and camera profile shared with local controllers.</summary>
    [Export] public PokerExperienceAuthoring ExperienceAuthoring;

    [ExportGroup("Stakes")]
    [Export] public int StartingStack = 500;
    [Export] public int SmallBlind = 5;
    [Export] public int BigBlind = 10;

    /// <summary>
    /// Blinds double every this many hands. Without it a session between cautious players never
    /// ends; with it the stacks are always eventually forced together.
    /// </summary>
    [Export] public int BlindIncreaseEveryHands = 12;

    /// <summary>
    /// Lets a lone host start a match to try the game out. Solo matches are excluded from the
    /// ranking, the same way a solo pool frame is.
    /// </summary>
    [Export] public bool AllowSoloDebug;

    public GlobalCamera Camera;
    public bool IsSoloMatch { get; private set; }

    // ---------------------------------------------------------------- public state

    /// <summary>Community cards turned so far. Only ever grows within a hand.</summary>
    public int[] Board = System.Array.Empty<int>();

    public PokerStreet Street = PokerStreet.Preflop;

    /// <summary>Who is still in the session, in seating order. Shrinks as players bust.</summary>
    public string[] SeatOrder = System.Array.Empty<string>();

    public readonly System.Collections.Generic.Dictionary<string, int> Stacks = new();
    public readonly System.Collections.Generic.Dictionary<string, int> BetThisRound = new();
    public readonly System.Collections.Generic.Dictionary<string, int> BetThisHand = new();

    /// <summary>
    /// Bet level immediately after each player last acted. Missing means they have not acted since
    /// the street opened or the last full raise. It is public betting history, not private data.
    /// </summary>
    public readonly System.Collections.Generic.Dictionary<string, int> BetLevelAfterLastAction = new();

    public readonly System.Collections.Generic.Dictionary<string, List<ChipRun>> ChipBanks = new();
    public readonly System.Collections.Generic.Dictionary<string, List<ChipRun>> RoundChipRuns = new();
    public readonly List<ChipRun> PotChipRuns = new();
    public readonly List<ChipRun> LastChipRuns = new();
    /// <summary>
    /// Server-approved chips currently staged by a player. This is public presentation state only;
    /// Stacks, ChipBanks and the pot remain unchanged until the normal action RPC is accepted.
    /// </summary>
    public readonly System.Collections.Generic.Dictionary<string, PokerPreparedWagerSnapshot>
        PreparedWagers = new();
    public readonly HashSet<string> Folded = new();
    public readonly HashSet<string> AllIn = new();

    /// <summary>Hole cards turned face up at a showdown. Empty at every other moment.</summary>
    public readonly System.Collections.Generic.Dictionary<string, int[]> RevealedHoleCards = new();

    /// <summary>What each shown hand was, so the table can say why it won.</summary>
    public readonly System.Collections.Generic.Dictionary<string, HandCategory> ShowdownCategories = new();

    public bool ShowdownWaiting;
    public readonly HashSet<string> PendingShowdownReveals = new();
    /// <summary>-1 during the initial grace period; otherwise visible seconds remaining.</summary>
    public int ShowdownCountdown = -1;
    public bool CardsCleaningUp;

    /// <summary>Who collected what from the hand that just finished.</summary>
    public readonly System.Collections.Generic.Dictionary<string, int> Winners = new();

    public int CurrentBet;
    public int MinRaiseIncrement;
    public int ButtonSeat;
    public int PotTotal;
    public int HandNumber;
    public int ActiveSmallBlind;
    public int ActiveBigBlind;

    /// <summary>Server-issued stamp for the current turn; a request carrying a stale one is dropped.</summary>
    public int TurnToken;

    public string LastAction = "";
    public string LastPlayer = "";
    public int LastAmount;

    /// <summary>
    /// Counts player actions. Changes exactly once per action, which is what a gesture keys off —
    /// the turn stamp cannot do that job, because one action can produce several contexts and
    /// several contexts can carry the same action.
    /// </summary>
    public int ActionSeq;

    /// <summary>This peer's own two cards. Empty on every peer that is not their owner.</summary>
    public int[] LocalHoleCards = System.Array.Empty<int>();

    /// <summary>
    /// Whether this peer's player has taken their cards off the cloth this hand.
    ///
    /// Purely local and deliberately not in any context: it is a gesture this player made with
    /// their own hands, and nobody else's table needs to know about it. The hand view sets it; the
    /// seat presenter reads it to know when to stop drawing the pair lying in front of them.
    /// </summary>
    public bool LocalPickedUpCards;

    private GameModeHandler _gameModeHandler;
    private readonly HashSet<string> _pendingReclaimedControllerPlayerIds = new();

    public override int MinimumPlayers => AllowSoloDebug ? 1 : 2;
    public override int MaximumPlayers => 4;

    public override bool CountsWinsForRanking => !IsSoloMatch;

    [Signal]
    public delegate void HudStateUpdatedEventHandler();

    [Signal]
    public delegate void LocalHandChangedEventHandler();

    [Signal]
    public delegate void PreparedWagersChangedEventHandler();

    public PokerTurnResolver Resolver =>
        _gameModeHandler?.CurrentGameMode?.TurnResolver as PokerTurnResolver;

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

        foreach (var playerId in _pendingReclaimedControllerPlayerIds.ToArray())
        {
            if (!IsMatchActive || !TurnOrder.Contains(playerId))
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

    public override void SetCamera(GlobalCamera camera) => Camera = camera;

    public Marker3D SeatFor(string playerId)
    {
        var index = SeatIndexFor(playerId);
        if (index < 0 || Seats == null || index >= Seats.GetChildCount())
            return null;

        return Seats.GetChild(index) as Marker3D;
    }

    /// <summary>Creates the configured card, retaining old scene overrides as a safe fallback.</summary>
    public PokerCard CreateCard(PackedScene fallback = null)
    {
        var scene = VisualAssets?.CardScene ?? fallback;
        return scene?.Instantiate() as PokerCard;
    }

    public PackedScene ChipScene => VisualAssets?.ChipScene;

    // ---------------------------------------------------------------- derived reads

    /// <summary>
    /// What is actually in the middle right now: the hand's total minus whatever is still sitting in
    /// front of the players this street.
    ///
    /// The pot pile draws this rather than the total, because a chip bet this street is already
    /// drawn at its owner's seat — showing the total as well put every one of those chips on the
    /// table twice. The HUD still says the TOTAL, which is what a player is playing for.
    /// </summary>
    public int PotInMiddle
    {
        get
        {
            var street = 0;
            foreach (var value in BetThisRound.Values)
                street += value;

            return Mathf.Max(0, PotTotal - street);
        }
    }

    public int StackOf(string playerId) =>
        !string.IsNullOrEmpty(playerId) && Stacks.TryGetValue(playerId, out var stack)
            ? stack : 0;

    public int BetOf(string playerId) =>
        !string.IsNullOrEmpty(playerId) && BetThisRound.TryGetValue(playerId, out var bet)
            ? bet : 0;

    public IReadOnlyList<ChipRun> ChipBankOf(string playerId) =>
        !string.IsNullOrEmpty(playerId) && ChipBanks.TryGetValue(playerId, out var runs)
            ? runs : System.Array.Empty<ChipRun>();

    public IReadOnlyList<ChipRun> RoundChipsOf(string playerId) =>
        !string.IsNullOrEmpty(playerId) && RoundChipRuns.TryGetValue(playerId, out var runs)
            ? runs : System.Array.Empty<ChipRun>();

    public PokerPreparedWagerSnapshot PreparedWagerOf(string playerId) =>
        playerId != null && PreparedWagers.TryGetValue(playerId, out var wager) ? wager : null;

    public bool HasFolded(string playerId) => Folded.Contains(playerId);

    public bool IsAllIn(string playerId) => AllIn.Contains(playerId);

    /// <summary>What it costs the local player to stay in right now.</summary>
    public int AmountToCall(string playerId) =>
        Mathf.Max(0, Mathf.Min(CurrentBet - BetOf(playerId), StackOf(playerId)));

    /// <summary>
    /// The local player's betting position, rebuilt from public state so the interface offers
    /// exactly what the server will accept.
    /// </summary>
    public PlayerBetState BetStateOf(string playerId) =>
        new()
        {
            PlayerId = playerId,
            Stack = StackOf(playerId),
            CommittedThisRound = BetOf(playerId),
            CommittedThisHand = !string.IsNullOrEmpty(playerId)
                                && BetThisHand.TryGetValue(playerId, out var committed)
                ? committed : 0,
            HasFolded = HasFolded(playerId),
            HasActedThisRound = !string.IsNullOrEmpty(playerId)
                                && BetLevelAfterLastAction.ContainsKey(playerId),
            BetLevelWhenLastActed = !string.IsNullOrEmpty(playerId)
                                    && BetLevelAfterLastAction.TryGetValue(playerId, out var level)
                ? level : 0,
        };

    /// <summary>
    /// A raise needs somebody with chips left to answer it. Once every opponent is folded or all-in,
    /// the remaining decision is only whether to call or fold an outstanding amount.
    /// </summary>
    public bool HasOpponentWhoCanAct(string playerId)
    {
        foreach (var otherPlayerId in SeatOrder)
        {
            if (otherPlayerId != playerId
                && !HasFolded(otherPlayerId)
                && StackOf(otherPlayerId) > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The hand is over and the table is showing the result. Nobody may act — the turn owner is
    /// still whoever spoke last, so without this the interface keeps offering them a call on a hand
    /// that has already paid out.
    /// </summary>
    public bool HandSettled => Winners.Count > 0;

    public bool LocalMustReveal
    {
        get
        {
            var playerId = Player == null ? null : (string)Player.Name;
            return ShowdownWaiting && playerId != null && PendingShowdownReveals.Contains(playerId);
        }
    }

    public bool IsSessionOver => SeatOrder.Length > 0 && CountWithChips() <= 1;

    private int CountWithChips()
    {
        var count = 0;
        foreach (var playerId in SeatOrder)
        {
            if (StackOf(playerId) > 0)
                count++;
        }

        return count;
    }

    // ---------------------------------------------------------------- match lifecycle

    public override void SetupMatch(Array players, string firstTurnOwner)
    {
        ClearPendingReclaimedControllers();
        PrepareMatch(players, firstTurnOwner);
        IsSoloMatch = players.Count == 1;

        ResetPublicState();
        BoardPresenter?.Clear();

        _gameModeHandler.Setup(this);

        // Clients equip their controllers and take their seats off this signal, so it has to reach
        // them before the deal does. The turn bridge mirrors it on the same reliable channel the
        // deal uses, which keeps that order.
        EmitSignal(SignalName.MatchStarted, players, firstTurnOwner);

        if (Multiplayer.IsServer())
            Resolver?.BeginSession(players);
    }

    private void ResetPublicState()
    {
        Board = System.Array.Empty<int>();
        Street = PokerStreet.Preflop;
        SeatOrder = System.Array.Empty<string>();
        Stacks.Clear();
        BetThisRound.Clear();
        BetThisHand.Clear();
        BetLevelAfterLastAction.Clear();
        ChipBanks.Clear();
        RoundChipRuns.Clear();
        PotChipRuns.Clear();
        LastChipRuns.Clear();
        PreparedWagers.Clear();
        Folded.Clear();
        AllIn.Clear();
        RevealedHoleCards.Clear();
        ShowdownCategories.Clear();
        Winners.Clear();
        ShowdownWaiting = false;
        PendingShowdownReveals.Clear();
        ShowdownCountdown = -1;
        CardsCleaningUp = false;
        CurrentBet = 0;
        MinRaiseIncrement = 0;
        ButtonSeat = 0;
        PotTotal = 0;
        HandNumber = 0;
        ActiveSmallBlind = SmallBlind;
        ActiveBigBlind = BigBlind;
        TurnToken = 0;
        LastAction = "";
        LastPlayer = "";
        LastAmount = 0;
        ActionSeq = 0;
        LocalHoleCards = System.Array.Empty<int>();
    }

    /// <summary>
    /// Takes the public snapshot the server packed into a turn context. Every state change already
    /// travels as a turn context, so this is the single place the table catches up — there is no
    /// second sync channel that could arrive out of order with the turn itself.
    /// </summary>
    public void ApplyPublicSnapshot(Dictionary context)
    {
        if (context == null || !context.ContainsKey("board"))
            return;

        Board = context["board"].AsInt32Array();
        Street = (PokerStreet)(int)context["street"];
        SeatOrder = context["seat_order"].AsStringArray();

        ReadTable(context, "stack_players", "stacks", Stacks);
        ReadTable(context, "round_players", "round_bets", BetThisRound);
        ReadTable(context, "hand_players", "hand_bets", BetThisHand);
        ReadTable(context, "acted_players", "acted_bet_levels", BetLevelAfterLastAction);
        ReadRunTable(context, "chip_bank_players", "chip_bank_denominations", "chip_bank_counts",
            ChipBanks);
        ReadRunTable(context, "round_chip_players", "round_chip_denominations", "round_chip_counts",
            RoundChipRuns);
        ReadRuns(context, "pot_chip_denominations", "pot_chip_counts", PotChipRuns);
        LastChipRuns.Clear();
        if (context.TryGetValue("last_chip_denominations", out var lastChips))
            LastChipRuns.AddRange(PokerChipStack.FromDenominations(lastChips.AsInt32Array()));
        var preparedWagerChanged = ReadPreparedWager(context);

        ReadSet(context, "folded", Folded);
        ReadSet(context, "all_in", AllIn);

        CurrentBet = (int)context["current_bet"];
        MinRaiseIncrement = (int)context["min_raise"];
        ButtonSeat = (int)context["button_seat"];
        PotTotal = (int)context["pot_total"];
        HandNumber = (int)context["hand_number"];
        ActiveSmallBlind = (int)context["small_blind"];
        ActiveBigBlind = (int)context["big_blind"];
        TurnToken = (int)context["turn_token"];

        LastAction = context.TryGetValue("last_action", out var action) ? (string)action : "";
        LastPlayer = context.TryGetValue("last_player", out var player) ? (string)player : "";
        LastAmount = context.TryGetValue("last_amount", out var amount) ? (int)amount : 0;
        ActionSeq = context.TryGetValue("action_seq", out var seq) ? (int)seq : 0;
        ShowdownWaiting = context.TryGetValue("showdown_waiting", out var waiting) && (bool)waiting;
        ReadSet(context, "showdown_pending", PendingShowdownReveals);
        ShowdownCountdown = context.TryGetValue("showdown_countdown", out var countdown)
            ? (int)countdown : -1;
        CardsCleaningUp = context.TryGetValue("card_cleanup", out var cleanup) && (bool)cleanup;

        ReadReveals(context);
        ReadResult(context);

        BoardPresenter?.Sync(Board, PotInMiddle, HandNumber, Street);
        if (preparedWagerChanged)
            EmitSignal(SignalName.PreparedWagersChanged);
        EmitSignal(SignalName.HudStateUpdated);
    }

    private bool ReadPreparedWager(Dictionary context)
    {
        PokerPreparedWagerSnapshot next = null;
        var playerId = context.TryGetValue("prepared_player", out var playerVariant)
            ? (string)playerVariant : "";
        if (!string.IsNullOrEmpty(playerId)
            && context.TryGetValue("prepared_denominations", out var denominationsVariant))
        {
            next = new PokerPreparedWagerSnapshot(
                playerId,
                context.TryGetValue("prepared_turn_token", out var turnVariant)
                    ? (int)turnVariant : 0,
                context.TryGetValue("prepared_revision", out var revisionVariant)
                    ? (int)revisionVariant : 0,
                denominationsVariant.AsInt32Array(),
                context.TryGetValue("prepared_committed_action_seq", out var committedVariant)
                    ? (int)committedVariant : -1);
        }

        PokerPreparedWagerSnapshot previous = null;
        if (PreparedWagers.Count == 1)
        {
            foreach (var entry in PreparedWagers)
            {
                previous = entry.Value;
                break;
            }
        }

        var changed = PreparedWagers.Count > 1
                      || (previous == null) != (next == null)
                      || (previous != null && !previous.SameAs(next));
        PreparedWagers.Clear();
        if (next != null)
            PreparedWagers[next.PlayerId] = next;

        return changed;
    }

    private static void ReadTable(
        Dictionary context, string keysName, string valuesName,
        System.Collections.Generic.Dictionary<string, int> into)
    {
        into.Clear();
        if (!context.ContainsKey(keysName) || !context.ContainsKey(valuesName))
            return;

        var keys = context[keysName].AsStringArray();
        var values = context[valuesName].AsInt32Array();

        for (var i = 0; i < keys.Length && i < values.Length; i++)
            into[keys[i]] = values[i];
    }

    private static void ReadSet(Dictionary context, string key, HashSet<string> into)
    {
        into.Clear();
        if (!context.ContainsKey(key))
            return;

        foreach (var playerId in context[key].AsStringArray())
            into.Add(playerId);
    }

    private static void ReadRunTable(
        Dictionary context, string playersName, string denominationsName, string countsName,
        System.Collections.Generic.Dictionary<string, List<ChipRun>> into)
    {
        into.Clear();
        if (!context.ContainsKey(playersName) || !context.ContainsKey(denominationsName)
            || !context.ContainsKey(countsName))
            return;

        var players = context[playersName].AsStringArray();
        var denominations = context[denominationsName].AsInt32Array();
        var counts = context[countsName].AsInt32Array();
        var length = Mathf.Min(players.Length, Mathf.Min(denominations.Length, counts.Length));
        for (var index = 0; index < length; index++)
        {
            if (!into.TryGetValue(players[index], out var runs))
                runs = into[players[index]] = new List<ChipRun>();
            runs.Add(new ChipRun(denominations[index], counts[index]));
        }
    }

    private static void ReadRuns(
        Dictionary context, string denominationsName, string countsName, List<ChipRun> into)
    {
        into.Clear();
        if (!context.ContainsKey(denominationsName) || !context.ContainsKey(countsName))
            return;
        var denominations = context[denominationsName].AsInt32Array();
        var counts = context[countsName].AsInt32Array();
        for (var index = 0; index < Mathf.Min(denominations.Length, counts.Length); index++)
            into.Add(new ChipRun(denominations[index], counts[index]));
    }

    /// <summary>
    /// Hole cards are only ever in a context at a showdown, and only for players who must show. Two
    /// ids per player, flattened, because Variant handles flat arrays cleanly.
    /// </summary>
    private void ReadReveals(Dictionary context)
    {
        RevealedHoleCards.Clear();
        if (!context.ContainsKey("reveal_players") || !context.ContainsKey("reveal_cards"))
            return;

        var players = context["reveal_players"].AsStringArray();
        var cards = context["reveal_cards"].AsInt32Array();

        for (var i = 0; i < players.Length; i++)
        {
            var first = i * PokerDeal.HoleCardCount;
            if (first + 1 >= cards.Length)
                break;

            RevealedHoleCards[players[i]] = new[] { cards[first], cards[first + 1] };
        }
    }

    /// <summary>
    /// How the finished hand was decided. Categories rather than the packed rank, because the table
    /// only needs to NAME the hand — comparing them was the server's job and it is already done.
    /// </summary>
    private void ReadResult(Dictionary context)
    {
        ShowdownCategories.Clear();
        Winners.Clear();

        if (context.ContainsKey("showdown_players") && context.ContainsKey("showdown_categories"))
        {
            var players = context["showdown_players"].AsStringArray();
            var categories = context["showdown_categories"].AsInt32Array();

            for (var i = 0; i < players.Length && i < categories.Length; i++)
                ShowdownCategories[players[i]] = (HandCategory)categories[i];
        }

        if (!context.ContainsKey("win_players") || !context.ContainsKey("win_amounts"))
            return;

        var winners = context["win_players"].AsStringArray();
        var amounts = context["win_amounts"].AsInt32Array();

        for (var i = 0; i < winners.Length && i < amounts.Length; i++)
            Winners[winners[i]] = amounts[i];
    }

    /// <summary>Called only on the peer the cards belong to, from a targeted RPC.</summary>
    public void ApplyLocalHoleCards(int[] cards)
    {
        LocalHoleCards = cards ?? System.Array.Empty<int>();
        EmitSignal(SignalName.LocalHandChanged);
        EmitSignal(SignalName.HudStateUpdated);
    }

    private void OnMatchStarts(Array playersIds, string firstTurnOwner)
    {
        if (GameControllerScene == null)
        {
            // Loud, because the cause is a wiring mistake in the scene: the match will run on the
            // server but nobody will be able to see or play it.
            GD.PushError("PokerGame sem GameControllerScene: ninguém vai conseguir jogar a mesa.");
            return;
        }

        foreach (var playerIdVariant in playersIds)
        {
            var player = PlayerRegistry.Instance.GetPlayerById((string)playerIdVariant);
            player?.GameHandler.EquipGameController(GameControllerScene, this, Camera);
        }
    }

    private void OnMatchIsOver(string winner, Dictionary context)
    {
        ClearPendingReclaimedControllers();

        // The final board and the showdown reach the table through this context rather than a turn
        // change, because a session-ending hand never hands the turn on.
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
        if (_pendingReclaimedControllerPlayerIds.Remove(playerId)
            && _pendingReclaimedControllerPlayerIds.Count == 0)
        {
            SetProcess(false);
        }

        // Their chips stay on the table until the server folds them out of the hand and rebuilds the
        // seating, which arrives with the next context. A peer can be one action behind on a stack;
        // it cannot be wrong about whose money it is.
        if (Multiplayer.IsServer())
            Resolver?.RemoveFromSession(playerId);

        EmitSignal(SignalName.HudStateUpdated);

        var leavingPlayer = PlayerRegistry.Instance.GetPlayerById(playerId);
        if (leavingPlayer?.GameHandler.CurrentController == null)
            return;

        leavingPlayer.GameHandler.CurrentController.GiveControl();
        leavingPlayer.GameHandler.UnequipCurrentController();
        leavingPlayer.TakeControl();
    }

    private void OnPlayerReclaimed(string oldPlayerId, string newPlayerId, Array turnOrder, Dictionary context)
    {
        // BuildHandoffContext is captured immediately before TableGame replaces the disconnected id
        // in TurnOrder. Its monetary snapshot is authoritative, but every player-keyed entry still
        // names the old connection. Apply one remapped copy now so existing peers do not spend the
        // rest of the turn showing an obsolete stack/bank (or a stale prepared wager) until somebody
        // happens to act. The reconnecting peer also receives the resolver's targeted full snapshot.
        var reclaimedContext = RemapReclaimedContext(context, oldPlayerId, newPlayerId);
        var appliedPublicContext = reclaimedContext.ContainsKey("board")
                                   && reclaimedContext.ContainsKey("seat_order");
        if (appliedPublicContext)
            ApplyPublicSnapshot(reclaimedContext);

        EquipOrDeferReclaimedController(newPlayerId, turnOrder);

        if (!appliedPublicContext)
            EmitSignal(SignalName.HudStateUpdated);
        // A reclaimed peer can arrive while this machine is halfway through chips, payout or showdown.
        // Rebuild from public state instead of attempting to resume a sequence with missing history.
        SeatPresenter?.SnapToAuthoritativeState();
        SeatPresenter?.Refresh();

        if (Multiplayer.IsServer())
            Resolver?.ReissueStateTo(oldPlayerId, newPlayerId);
    }

    /// <summary>
    /// Reclaim replication and Player spawning use independent network paths. Public/secret state is
    /// migrated immediately; if the replacement node is not registered yet, presentation retries on
    /// subsequent frames while the match and seat remain valid.
    /// </summary>
    internal void EquipOrDeferReclaimedController(string playerId, Array turnOrder)
    {
        if (TryEquipReclaimedController(playerId))
        {
            _pendingReclaimedControllerPlayerIds.Remove(playerId);
            if (_pendingReclaimedControllerPlayerIds.Count == 0)
                SetProcess(false);
            return;
        }

        if (GameControllerScene == null
            || !IsMatchActive
            || turnOrder == null
            || !turnOrder.Contains(playerId))
        {
            return;
        }

        _pendingReclaimedControllerPlayerIds.Add(playerId);
        SetProcess(true);
    }

    internal int PendingReclaimedControllerCount =>
        _pendingReclaimedControllerPlayerIds.Count;

    private bool TryEquipReclaimedController(string playerId)
    {
        var registry = PlayerRegistry.Instance;
        if (registry == null
            || !registry.TryGetPlayerById(playerId, out var player))
        {
            return false;
        }

        AttachLocalReclaimedPlayer(playerId, player);

        if (GameControllerScene == null || player.GameHandler == null)
            return false;

        // Reliable reclaim state can be replayed, and the spawn retry can observe a controller
        // equipped by another ordered packet. Replacing that live controller would discard its
        // local hand/input state and could duplicate placement continuations.
        if (player.GameHandler.CurrentTableGame != this
            || !IsInstanceValid(player.GameHandler.CurrentController))
        {
            player.GameHandler.EquipGameController(GameControllerScene, this, Camera);
        }

        return true;
    }

    private void ClearPendingReclaimedControllers()
    {
        _pendingReclaimedControllerPlayerIds.Clear();
        SetProcess(false);
    }

    /// <summary>
    /// Re-keys the public handoff snapshot captured immediately before TableGame replaces a reclaimed
    /// peer id. Poker contexts are flat, so remapping scalar strings and packed string arrays covers
    /// seats, stacks, bets, banks, reveals, awards and any active prepared-wager owner together.
    /// </summary>
    internal static Dictionary RemapReclaimedContext(
        Dictionary context, string oldPlayerId, string newPlayerId)
    {
        var remapped = context?.Duplicate() ?? new Dictionary();
        if (string.IsNullOrEmpty(oldPlayerId) || string.IsNullOrEmpty(newPlayerId)
            || oldPlayerId == newPlayerId)
            return remapped;

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

            var players = value.AsStringArray();
            var changed = false;
            for (var index = 0; index < players.Length; index++)
            {
                if (players[index] != oldPlayerId)
                    continue;
                players[index] = newPlayerId;
                changed = true;
            }

            if (changed)
                remapped[key] = players;
        }

        return remapped;
    }
}
