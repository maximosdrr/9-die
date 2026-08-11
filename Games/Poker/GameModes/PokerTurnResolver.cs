using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;
using Poker.Rules;

/// <summary>
/// Runs a Hold'em session server side.
///
/// The private half — the hole cards, the deal seed, the turn stamp and the one targeted channel a
/// holding travels on — is <see cref="SecretHandTurnResolver"/>'s. Added here is the game: blinds,
/// streets, the betting round, the showdown and the session that runs until one player has
/// everything.
///
/// <see cref="_board"/> joins the inherited secrets. All five community cards are dealt at the
/// moment of the shuffle and simply revealed a street at a time — no card is ever CHOSEN after
/// somebody has seen a bet, which is the property that makes the deal defensible.
/// </summary>
[GlobalClass]
public partial class PokerTurnResolver : SecretHandTurnResolver
{
    internal const int ActionRequestsPerSecond = 8;
    internal const int PreparedWagerRequestsPerSecond = 24;
    internal const int MaximumPreparedWagerChips = 64;
    internal const int MaximumTrackedActionPeers = 16;

    [Export] public PokerPresentationProfile PresentationProfile;
    private PokerPresentationProfile Profile =>
        PresentationProfile ??= new PokerPresentationProfile();

    /// <summary>How long the table sits on a finished hand before the next one is dealt.</summary>
    // Includes the exposed-hand reading beat and still leaves roughly eight seconds for the ranked
    // best-five comparison before the next hand clears the cloth.
    [Export] public float ShowdownSeconds = 12.0f;

    /// <summary>Pause after a hand that ended with everyone folding — nothing to read, so shorter.</summary>
    [Export] public float FoldedHandSeconds = 4.5f;

    /// <summary>Production advances automatically; visual harnesses may hold a result indefinitely.</summary>
    [Export] public bool AutoAdvanceHands = true;

    [ExportGroup("Showdown decision")]
    /// <summary>Time to reveal voluntarily before the visible countdown starts.</summary>
    [Export] public float ShowdownRevealGraceSeconds = 10.0f;
    /// <summary>Visible final window; pending hands are exposed automatically when it reaches zero.</summary>
    [Export] public float ShowdownRevealCountdownSeconds = 10.0f;

    /// <summary>
    /// A reversible chip-preview request has its own revision in addition to the turn stamp. Keeping
    /// both values in the refusal lets the local controller ignore an old rejection after the player
    /// has already corrected the selection.
    /// </summary>
    [Signal]
    public delegate void PreparedWagerRejectedEventHandler(
        int turnToken, int revision, string reason);

    public PokerGame Game => Table as PokerGame;
    private readonly PeerRequestRateLimiter _actionRequestLimiter = new(
        ActionRequestsPerSecond, 1000, MaximumTrackedActionPeers);
    private readonly PeerRequestRateLimiter _preparedWagerRequestLimiter = new(
        PreparedWagerRequestsPerSecond, 1000, MaximumTrackedActionPeers);

    /// <summary>All five community cards, dealt up front and revealed a street at a time.</summary>
    private readonly List<int> _board = new();

    /// <summary>Betting state per seat, parallel to <see cref="_seatOrder"/>.</summary>
    private readonly List<PlayerBetState> _bets = new();

    // Monetary totals decide the rules; this ledger decides which physical chips represent them.
    // It is server-owned so peers never independently decompose the same amount into different chips.
    private readonly System.Collections.Generic.Dictionary<string, List<ChipRun>> _chipBanks = new();
    private readonly System.Collections.Generic.Dictionary<string, List<ChipRun>> _roundChipRuns = new();
    private readonly List<ChipRun> _potChipRuns = new();
    private List<ChipRun> _lastChipRuns = new();

    // A prepared wager is public table theatre, not money. It remains separate from _chipBanks and
    // _bets until TryAction accepts the normal poker action that names the exact same denominations.
    private string _preparedWagerPlayer = "";
    private int _preparedWagerTurnToken = -1;
    private int _preparedWagerRevision = -1;
    private int[] _preparedWagerDenominations = System.Array.Empty<int>();
    private int _preparedWagerCommittedActionSeq = -1;

    /// <summary>Who is still in the session, in seating order. Shrinks as players bust.</summary>
    private readonly List<string> _seatOrder = new();

    private readonly System.Collections.Generic.Dictionary<string, int[]> _reveals = new();

    /// <summary>What each shown hand actually was, so the table can say WHY it won.</summary>
    private readonly System.Collections.Generic.Dictionary<string, PokerHandRank> _showdownRanks = new();
    private readonly HashSet<string> _pendingShowdownReveals = new();

    /// <summary>Who collected what from the last hand.</summary>
    private readonly System.Collections.Generic.Dictionary<string, int> _awards = new();

    private PokerStreet _street;
    private int _buttonSeat;
    private int _actingSeat = -1;
    private int _currentBet;
    private int _minRaiseIncrement;
    private int _handNumber;
    private int _smallBlind;
    private int _bigBlind;
    private bool _handInProgress;
    private bool _awaitingShowdownReveals;
    private float _showdownRevealElapsed;
    private int _publishedShowdownCountdown = int.MinValue;
    private bool _cardCleanupActive;
    private float _scheduledCardCleanupSeconds;
    private ulong _handPauseRevision;

    private int _actionSeq;
    private string _lastAction = "";
    private string _lastPlayer = "";
    private int _lastAmount;

    protected override void ResetSecretState()
    {
        _handPauseRevision++;
        _actionRequestLimiter.Clear();
        _preparedWagerRequestLimiter.Clear();
        _board.Clear();
        _bets.Clear();
        _chipBanks.Clear();
        _roundChipRuns.Clear();
        _potChipRuns.Clear();
        _lastChipRuns.Clear();
        ResetPreparedWagerState();
        _seatOrder.Clear();
        ClearHandResult();
        _street = PokerStreet.Preflop;
        _buttonSeat = 0;
        _actingSeat = -1;
        _currentBet = 0;
        _minRaiseIncrement = 0;
        _handNumber = 0;
        _handInProgress = false;
        _awaitingShowdownReveals = false;
        _showdownRevealElapsed = 0.0f;
        _publishedShowdownCountdown = int.MinValue;
        _pendingShowdownReveals.Clear();
        _cardCleanupActive = false;
        _scheduledCardCleanupSeconds = 0.0f;
        _lastAction = "";
        _lastPlayer = "";
        _lastAmount = 0;
        _actionSeq = 0;
    }

    protected override void ClearSecretState()
    {
        _board.Clear();
        ResetPreparedWagerState();
    }

    public override void _Process(double delta)
    {
        if (Multiplayer.IsServer() && _awaitingShowdownReveals)
            AdvanceShowdownRevealClock((float)delta);
    }

    protected override void ApplyLocalHand(int[] items) => Game?.ApplyLocalHoleCards(items);

    protected override void ApplyPublicSnapshot(Dictionary context) => Game?.ApplyPublicSnapshot(context);

    protected override void ApplyFullSnapshot(Dictionary context)
    {
        Game?.ApplyPublicSnapshot(context);
        Game?.SeatPresenter?.SnapToAuthoritativeState();
        // ApplyPublicSnapshot refreshes once before the recovery snap. The snap deliberately discards
        // every in-flight actor, including a replicated reversible wager, so reconcile once more from
        // the just-applied authoritative snapshot. Without this final pass a late/reconnected peer kept
        // the wager in PokerGame.PreparedWagers but did not draw it until some unrelated later update.
        Game?.SeatPresenter?.Refresh();
    }

    protected override Dictionary BuildSnapshot() =>
        BuildContext(_lastAction, _lastPlayer, _lastAmount, advanceTurn: false);
}
