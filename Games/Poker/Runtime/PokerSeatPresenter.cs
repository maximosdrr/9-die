using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;
using ChipBatchPhase = PokerChipAnimator.Phase;

/// <summary>
/// Everything each seat owns, said with objects on the table instead of a panel: two face-down
/// cards, the chips they have pushed in, the stack they still hold, their name, dealer role and turn.
///
/// All of it is derived from <see cref="PokerGame"/>'s PUBLIC state — stacks, bets and who folded,
/// never a card anyone still holds — so every peer draws the same table from what it already has and
/// no message is added to the protocol for any of it. The one exception is a showdown, where the
/// server has deliberately made some hole cards public and they are turned face up here.
/// </summary>
/// <remarks>
/// The inspector contract and lifecycle live here. Responsibility-named partials beside this file
/// own cards, chip state, chip motion and the remaining table visuals.
/// </remarks>
[GlobalClass]
public partial class PokerSeatPresenter : Node3D
{
    /// <summary>
    /// Reconnect/new-hand policy: never replay stale local events. Discard unfinished motion and
    /// rebuild the latest public board, bets, banks and organized pot in one hidden update.
    /// </summary>
    public const string InterruptedPresentationPolicy = "SnapToAuthoritativeState";

    [Export] public PackedScene CardScene;
    [Export] public PokerBoardPresenter BoardPresenter;
    [Export] public Node3D Seats;
    [Export] public PokerPresentationProfile PresentationProfile;

    [ExportGroup("Labels")]
    [Export] public float NameHeight = 0.16f;
    [Export] public Color TurnColor = new(1.0f, 0.478431f, 0.2f);
    [Export] public Color IdleColor = new(0.678431f, 0.752941f, 0.839216f);
    [Export] public Color FoldedColor = new(0.45f, 0.47f, 0.52f);
    [Export] public float DealerLabelHeightAboveHead = 0.24f;

    [ExportGroup("Turn ring")]
    [Export] public float TurnRingRadius = 0.615f;
    [Export] public float TurnRingWidth = 0.0045f;
    [Export] public float TurnRingHeight = 0.003f;
    [Export] public float TurnRingGapDegrees = 8.0f;
    [Export] public int TurnRingArcSteps = 20;
    [Export] public Color ActiveTurnRingColor = new(0.24f, 0.66f, 0.36f, 0.48f);
    [Export] public Color OccupiedTurnRingColor = new(0.68f, 0.28f, 0.25f, 0.30f);
    [Export] public Color EmptyTurnRingColor = new(0.52f, 0.54f, 0.58f, 0.20f);

    private PokerGame _game;

    [ExportGroup("Dealing")]
    /// <summary>Seconds for one card to travel from the deck to a seat.</summary>
    [Export] public float DealSeconds = 0.52f;

    /// <summary>Gap between cards leaving the deck, so they go round the table one at a time.</summary>
    [Export] public float DealStagger = 0.34f;

    /// <summary>How high a card arcs on the way over, so it is thrown rather than dragged.</summary>
    [Export] public float DealArc = 0.040f;

    /// <summary>
    /// How long an opponent's pair sits on the cloth before they take it up. The local player's
    /// stays until they pick it up themselves.
    /// </summary>
    [Export] public float OpponentPickUpDelay = 1.1f;

    [ExportGroup("Folding")]
    /// <summary>How long the thrown pair takes to reach the muck.</summary>
    [Export] public float MuckSeconds = 0.55f;

    /// <summary>How high it arcs on the way, so it is thrown rather than pushed.</summary>
    [Export] public float MuckArc = 0.045f;

    /// <summary>How far apart discarded cards end up, so the muck reads as a heap.</summary>
    [Export] public float MuckSpread = 0.035f;

    /// <summary>Roughly where a seated player holds their cards, measured from the cloth.</summary>
    [Export] public float HandHeight = 0.17f;

    /// <summary>A seat's two cards and how far along their deal is. Purely local presentation.</summary>
    private sealed class SeatHand
    {
        public readonly PokerCard[] Cards = new PokerCard[PokerDeal.HoleCardCount];
        public readonly float[] Dealt = new float[PokerDeal.HoleCardCount];
        public readonly float[] Wait = new float[PokerDeal.HoleCardCount];
        public int Hand = -1;
        public float OnTable;
        public bool Revealed;

        /// <summary>Whether this pair has been thrown away.</summary>
        public bool Folded;

        /// <summary>How far along that throw is: 0 the instant it leaves, 1 lying in the muck.</summary>
        public float Mucked;

        /// <summary>
        /// Whether the throw started from the player's hands or off the cloth. Recorded when they
        /// fold, because whether the pair had been picked up stops being knowable afterwards.
        /// </summary>
        public bool FromHands;

        /// <summary>The local peer has reparented these exact nodes into its first-person grip.</summary>
        public bool InFirstPerson;

        /// <summary>Exact local transforms captured when cards leave the first-person grip.</summary>
        public readonly Transform3D[] ReleasedFrom = new Transform3D[PokerDeal.HoleCardCount];
        public bool Returning;
        public float Returned = 1.0f;
        public readonly Transform3D[] CleanupFrom = new Transform3D[PokerDeal.HoleCardCount];
        public readonly bool[] CleanupActive = new bool[PokerDeal.HoleCardCount];
        public readonly bool[] CleanupFaceDown = new bool[PokerDeal.HoleCardCount];
        public readonly int[] CleanupSlot = new int[PokerDeal.HoleCardCount];
    }

    [ExportGroup("Betting chips")]
    /// <summary>How long chips take to go from a stack to the middle.</summary>
    public float ChipFlightSeconds { get => Profile.ChipFlightSeconds; set => Profile.ChipFlightSeconds = value; }

    /// <summary>Height of the short toss/push above the felt.</summary>
    [Export] public float ChipFlightArc = 0.042f;

    /// <summary>How far a thrown chip ends up from the middle of its stack.</summary>
    [Export] public float ChipScatter = 0.011f;

    /// <summary>How tidily a player keeps their OWN stack. Not perfectly, but close.</summary>
    [Export] public float StackScatter = 0.0025f;

    /// <summary>Moves the bank inward and sideways so it is visible beside, not behind, the cards.</summary>
    [Export] public float StackInset = 0.090f;
    [Export] public float StackSideOffset = 0.150f;
    /// <summary>
    /// A committed bet stays directly in front of its owner, between their cards and the centre.
    /// The larger radial distance now provides the clearance; a lateral offset would collide with
    /// the reader-relative deck for one of the side chairs.
    /// </summary>
    [Export] public float BetSideOffset = 0.0f;
    [Export] public float BankColumnSpacing = 0.034f;

    [ExportGroup("Presentation sequence")]
    public float ChipLandingSeconds { get => Profile.ChipLandingSeconds; set => Profile.ChipLandingSeconds = value; }
    public float ChipCollectSeconds { get => Profile.ChipCollectSeconds; set => Profile.ChipCollectSeconds = value; }
    public float ChipOrganizeSeconds { get => Profile.ChipOrganizeSeconds; set => Profile.ChipOrganizeSeconds = value; }
    public float ChipCollectStagger { get => Profile.ChipCollectStagger; set => Profile.ChipCollectStagger = value; }
    public float ChipPayoutSeconds { get => Profile.ChipPayoutSeconds; set => Profile.ChipPayoutSeconds = value; }
    public float ChipPayoutStagger { get => Profile.ChipPayoutStagger; set => Profile.ChipPayoutStagger = value; }
    /// <summary>How far inward from the bank payout chips land loose before being stacked.</summary>
    [Export] public float WinnerLooseLandingInset = 0.060f;
    [Export] public float PotColumnSpacing = 0.050f;
    /// <summary>
    /// Maximum ordinary number of independently moving chip groups. Above this, chips of the same
    /// denomination travel together; denomination columns and represented value remain exact.
    /// </summary>
    public int MaxAnimatedChipGroups { get => Profile.MaxAnimatedChipGroups; set => Profile.MaxAnimatedChipGroups = value; }
    [Export] public int ChipBatchPoolSize = PokerPresentationTiming.DefaultMaxAnimatedChipGroups;
    [Export] public int PrewarmedChipsPerBatch = 1;

    [ExportGroup("Showdown comparison")]
    /// <summary>Time for an exposed pair to travel from the player's hands to the cloth.</summary>
    public float ShowdownRevealMotionSeconds { get => Profile.ShowdownRevealMotionSeconds; set => Profile.ShowdownRevealMotionSeconds = value; }
    /// <summary>Time left for everyone to read the exposed hole cards before ranking rearranges them.</summary>
    public float ShowdownRevealHoldSeconds { get => Profile.ShowdownRevealHoldSeconds; set => Profile.ShowdownRevealHoldSeconds = value; }
    /// <summary>Centre-to-centre distance between the two cards exposed in front of their owner.</summary>
    [Export] public float ShowdownPairSpacing = 0.054f;
    /// <summary>Small table-plane variation that keeps exposed pairs from looking mechanically placed.</summary>
    [Export] public float ShowdownPairPositionJitter = 0.006f;
    /// <summary>Maximum clockwise/counter-clockwise variation of each exposed card.</summary>
    [Export(PropertyHint.Range, "0,8,0.25")] public float ShowdownPairAngleJitterDegrees = 4.0f;
    /// <summary>Physical layer separation for overlapping cards, preventing coplanar depth flicker.</summary>
    [Export] public float ShowdownPairLayerSeparation = 0.0012f;
    public float ShowdownCardSeconds { get => Profile.ShowdownCardSeconds; set => Profile.ShowdownCardSeconds = value; }
    public float ShowdownRowStagger { get => Profile.ShowdownRowStagger; set => Profile.ShowdownRowStagger = value; }
    public float ShowdownCardStagger { get => Profile.ShowdownCardStagger; set => Profile.ShowdownCardStagger = value; }
    [Export] public float ShowdownRowSpacing = 0.118f;
    [Export] public float ShowdownCardSpacing = 0.075f;
    [Export] public float ShowdownArc = 0.034f;
    [Export] public Color ShowdownWinnerColor = new(0.35f, 1.0f, 0.48f);
    [Export] public Color ShowdownOtherColor = new(0.88f, 0.91f, 0.96f);

    [ExportGroup("Table sounds")]
    /// <summary>Short, dry knuckle impact used by the check/pass action.</summary>
    [Export] public AudioStream KnockSound;
    [Export(PropertyHint.Range, "-24,0,0.5")] public float KnockVolumeDb = -4.0f;

    /// <summary>Chip-on-felt recording. Nearby arrivals are merged before this sample is played.</summary>
    [Export] public AudioStream ChipLandingSound;
    [Export(PropertyHint.Range, "1,3,1")] public int ChipImpactVoiceLimit = 3;
    [Export(PropertyHint.Range, "-30,-3,0.5")] public float ChipSingleImpactDb = -17.0f;
    [Export(PropertyHint.Range, "-24,-3,0.5")] public float ChipMaximumImpactDb = -11.0f;

    [ExportGroup("Dealer change")]
    [Export] public AudioStream DealerChangeSound;
    [Export] public AnimationPlayer DealerAnimator;
    [Export] public string DealerChangeAnimation = "ExchangeChips";

    private readonly struct PendingChipAction
    {
        public readonly string PlayerId;
        public readonly int Amount;
        public readonly PokerStreet Street;
        public readonly int StackAfter;
        public readonly List<ChipRun> Runs;

        public PendingChipAction(
            string playerId, int amount, PokerStreet street, int stackAfter,
            IReadOnlyList<ChipRun> runs)
        {
            PlayerId = playerId;
            Amount = amount;
            Street = street;
            StackAfter = stackAfter;
            Runs = runs?.Select(run => new ChipRun(run.Denomination, run.Count)).ToList()
                ?? new List<ChipRun>();
        }
    }

    private readonly Dictionary<string, SeatHand> _holeCards = new();
    private bool _lastPickedUp;
    private int _lastGestureToken = -1;
    private AudioStreamPlayer3D _knock;
    private AudioStreamPlayer3D _dealerChangeAudio;

    /// <summary>
    /// Whether this peer's own pair has finished arriving. The hand view waits on it before playing
    /// the pick-up, so the player never reaches for a card that is still in the air.
    /// </summary>
    public bool LocalHandLanded { get; private set; }
    private readonly Dictionary<string, PokerChipPile> _stacks = new();
    private readonly Dictionary<string, Label3D> _names = new();
    private PokerChipAnimator _chipAnimator;
    private PokerChipSoundscape _chipSoundscape;
    private readonly Queue<PendingChipAction> _pendingChipActions = new();
    private readonly Dictionary<string, int> _observedStacks = new();
    private readonly Dictionary<string, int> _observedCommitted = new();
    private readonly Dictionary<string, int> _displayStacks = new();
    private readonly Dictionary<string, List<ChipRun>> _bankRuns = new();
    private PokerShowdownPresenter _showdownPresenter;
    private PokerPayoutSequencer _payoutSequencer;
    private int _presentationHand = -1;
    private int _presentationActionSeq = -1;
    private PokerStreet _visibleStreet = PokerStreet.Preflop;
    private PokerStreet _requestedStreet = PokerStreet.Preflop;
    private bool _collecting;
    private bool _organizing;
    private bool _collectionRequested;
    private bool _settlementCollected;
    private bool _lastPresentationReady;
    private int _nextChipSequence;
    private bool _cardCleanupActive;
    private float _cardCleanupElapsed;
    public bool CardsReturningToDeck => _cardCleanupActive;
    public int ReturningCardCount
    {
        get
        {
            var count = BoardPresenter?.ReturningCardCount ?? 0;
            count += _showdownPresenter?.CleanupCardCount ?? 0;
            foreach (var hand in _holeCards.Values)
            {
                foreach (var active in hand.CleanupActive)
                {
                    if (active)
                        count++;
                }
            }
            return count;
        }
    }
    private readonly List<MeshInstance3D> _turnRingSegments = new();
    private readonly List<StandardMaterial3D> _turnRingMaterials = new();
    public IReadOnlyList<MeshInstance3D> TurnRingSegments => _turnRingSegments;
    private Label3D _dealerLabel;
    public bool LastRecoveryDiscardedAnimation { get; private set; }
    private PokerPresentationProfile Profile => PresentationProfile ??= new PokerPresentationProfile();

    public override void _Ready()
    {
        _game = GetParent<PokerGame>();
        if (_game == null)
            return;

        SignalUtil.ConnectGuarded(_game, PokerGame.SignalName.HudStateUpdated,
            new Callable(this, MethodName.Refresh));

        BuildChipBatchPool();
        BuildPresentationComponents();
    }

    public override void _ExitTree()
    {
        if (_game == null)
            return;

        SignalUtil.DisconnectGuarded(_game, PokerGame.SignalName.HudStateUpdated,
            new Callable(this, MethodName.Refresh));
    }

    public void Refresh()
    {
        if (_game == null || Seats == null || BoardPresenter == null)
            return;

        var spec = BoardPresenter.Spec;
        CaptureChipPresentation(spec);
        if (_game.CardsCleaningUp)
            BeginCardCleanup();
        else if (_cardCleanupActive)
            EndCardCleanup();
        var seen = new HashSet<string>();

        for (var index = 0; index < _game.SeatOrder.Length; index++)
        {
            var playerId = _game.SeatOrder[index];
            var seat = SeatNodeFor(playerId);
            if (seat == null)
                continue;

            seen.Add(playerId);

            var toSeat = ToLocal(seat.GlobalPosition);
            var facing = new Vector2(toSeat.X, toSeat.Z);
            if (facing.LengthSquared() < 1e-6f)
                continue;

            facing = facing.Normalized();

            if (!_cardCleanupActive)
                RefreshHoleCards(playerId, facing, spec);
            RefreshChips(playerId, facing, spec);
            RefreshName(playerId, facing);
        }

        DropStale(seen);
        RefreshTurnRing();
        RefreshDealerLabel();
        PlayActionGesture();
    }

    // Per-frame coordination stays here; each visual subsystem owns its implementation in a
    // responsibility-named partial file beside this one.
    public override void _Process(double delta)
    {
        if (_game == null)
            return;

        // Taking the cards up is a purely local gesture and emits no state signal, so nothing else
        // would ever tell this presenter to stop drawing the pair lying on the cloth.
        var moved = _game.LocalPickedUpCards != _lastPickedUp;
        _lastPickedUp = _game.LocalPickedUpCards;
        moved |= AdvancePreparedWager((float)delta);

        var localId = _game.Player == null ? null : (string)_game.Player.Name;
        LocalHandLanded = false;

        if (_cardCleanupActive)
        {
            moved |= AdvanceCardCleanup((float)delta);
            moved |= _showdownPresenter?.Advance((float)delta, blocked: false) ?? false;
            if (moved)
                Refresh();
            return;
        }

        foreach (var entry in _holeCards)
        {
            var hand = entry.Value;
            var landed = true;

            if (hand.Returning && hand.Returned < 1.0f)
            {
                hand.Returned = Mathf.Min(1.0f,
                    hand.Returned + (float)delta / Mathf.Max(ShowdownRevealMotionSeconds, 0.01f));
                moved = true;
                if (hand.Returned >= 1.0f)
                    hand.Returning = false;
            }

            if (hand.Folded && hand.Mucked < 1.0f)
            {
                var throwStep = MuckSeconds <= 0.0f ? 1.0f : (float)delta / MuckSeconds;
                hand.Mucked = Mathf.MoveToward(hand.Mucked, 1.0f, throwStep);
                moved = true;
            }

            for (var i = 0; i < hand.Cards.Length; i++)
            {
                if (hand.Wait[i] > 0.0f)
                {
                    hand.Wait[i] -= (float)delta;
                    landed = false;
                    moved = true;
                    continue;
                }

                if (Mathf.IsEqualApprox(hand.Dealt[i], 1.0f))
                    continue;

                var step = DealSeconds <= 0.0f ? 1.0f : (float)delta / DealSeconds;
                hand.Dealt[i] = Mathf.MoveToward(hand.Dealt[i], 1.0f, step);
                landed = false;
                moved = true;
            }

            if (!landed)
                continue;

            if (entry.Key == localId)
                LocalHandLanded = true;

            // Only starts counting once the whole pair is down, so an opponent never appears to take
            // a card that has not arrived.
            var before = hand.OnTable;
            hand.OnTable += (float)delta;

            if (before <= OpponentPickUpDelay && hand.OnTable > OpponentPickUpDelay)
                moved = true;
        }

        moved |= AdvanceChipPresentation((float)delta);
        var showdownBlocked = _collecting || _organizing || _collectionRequested
            || HasPhase(ChipBatchPhase.ToBet, ChipBatchPhase.Landing,
                ChipBatchPhase.ToPot, ChipBatchPhase.Organizing)
            || _holeCards.Values.Any(hand => hand.Returning);
        moved |= _showdownPresenter?.Advance((float)delta, showdownBlocked) ?? false;

        // Only when something actually changed: this presenter redraws every seat's chips and
        // labels, and there is no reason to pay for that on a still table.
        if (moved)
            Refresh();

        var ready = PresentationReadyForAction;
        if (ready != _lastPresentationReady)
        {
            _lastPresentationReady = ready;
            _game.EmitSignal(PokerGame.SignalName.HudStateUpdated);
        }
    }
}
