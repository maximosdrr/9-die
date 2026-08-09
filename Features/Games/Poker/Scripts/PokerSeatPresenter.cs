using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;
using ChipBatch = PokerChipAnimator.Batch;
using ChipBatchPhase = PokerChipAnimator.Phase;

/// <summary>
/// Everything each seat owns, said with objects on the table instead of a panel: two face-down
/// cards, the chips they have pushed in, the stack they still hold, their name and count, and the
/// dealer button.
///
/// All of it is derived from <see cref="PokerGame"/>'s PUBLIC state — stacks, bets and who folded,
/// never a card anyone still holds — so every peer draws the same table from what it already has and
/// no message is added to the protocol for any of it. The one exception is a showdown, where the
/// server has deliberately made some hole cards public and they are turned face up here.
/// </summary>
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

	[ExportGroup("Dealer button")]
	[Export] public float ButtonRadius = 0.028f;
	[Export] public float ButtonOffset = 0.10f;

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
	[Export] public float StackInset = 0.055f;
	[Export] public float StackSideOffset = 0.105f;
	[Export] public float BankColumnSpacing = 0.034f;

	[ExportGroup("Presentation sequence")]
	public float ChipLandingSeconds { get => Profile.ChipLandingSeconds; set => Profile.ChipLandingSeconds = value; }
	public float ChipCollectSeconds { get => Profile.ChipCollectSeconds; set => Profile.ChipCollectSeconds = value; }
	public float ChipOrganizeSeconds { get => Profile.ChipOrganizeSeconds; set => Profile.ChipOrganizeSeconds = value; }
	public float ChipCollectStagger { get => Profile.ChipCollectStagger; set => Profile.ChipCollectStagger = value; }
	public float ChipPayoutSeconds { get => Profile.ChipPayoutSeconds; set => Profile.ChipPayoutSeconds = value; }
	public float ChipPayoutStagger { get => Profile.ChipPayoutStagger; set => Profile.ChipPayoutStagger = value; }
	[Export] public float PotColumnSpacing = 0.050f;
	/// <summary>
	/// Maximum ordinary number of independently moving chip groups. Above this, chips of the same
	/// denomination travel together; denomination columns and represented value remain exact.
	/// </summary>
	public int MaxAnimatedChipGroups { get => Profile.MaxAnimatedChipGroups; set => Profile.MaxAnimatedChipGroups = value; }
	[Export] public int ChipBatchPoolSize = PokerPresentationTiming.DefaultMaxAnimatedChipGroups;
	[Export] public int PrewarmedChipsPerBatch = 1;

	[ExportGroup("Showdown comparison")]
	/// <summary>Time left for everyone to read the exposed hole cards before ranking rearranges them.</summary>
	public float ShowdownRevealHoldSeconds { get => Profile.ShowdownRevealHoldSeconds; set => Profile.ShowdownRevealHoldSeconds = value; }
	public float ShowdownCardSeconds { get => Profile.ShowdownCardSeconds; set => Profile.ShowdownCardSeconds = value; }
	public float ShowdownRowStagger { get => Profile.ShowdownRowStagger; set => Profile.ShowdownRowStagger = value; }
	public float ShowdownCardStagger { get => Profile.ShowdownCardStagger; set => Profile.ShowdownCardStagger = value; }
	[Export] public float ShowdownRowSpacing = 0.118f;
	[Export] public float ShowdownCardSpacing = 0.075f;
	[Export] public float ShowdownArc = 0.034f;
	[Export] public Color ShowdownWinnerColor = new(0.35f, 1.0f, 0.48f);
	[Export] public Color ShowdownOtherColor = new(0.88f, 0.91f, 0.96f);

	/// <summary>Placeholder knuckle on wood. Any short, dry hit reads correctly.</summary>
	[Export] public AudioStream KnockSound;

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

		public PendingChipAction(string playerId, int amount, PokerStreet street, int stackAfter)
		{
			PlayerId = playerId;
			Amount = amount;
			Street = street;
			StackAfter = stackAfter;
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
	private MeshInstance3D _dealerButton;
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

			RefreshHoleCards(playerId, facing, spec);
			RefreshChips(playerId, facing, spec);
			RefreshName(playerId, facing);
		}

		DropStale(seen);
		RefreshDealerButton(spec);
		PlayActionGesture();
	}

	/// <summary>
	/// A seat's two cards: thrown from the deck at the start of the hand, lying face down until
	/// they are taken up, thrown into the muck if their owner gives up, and back on the cloth face up
	/// at a showdown.
	///
	/// They do NOT sit here face down for the whole hand. Who is still in is already on the felt as
	/// a bet and a name; two blank rectangles per seat for the rest of the hand only crowded the
	/// table. What earns its place is the moment they arrive, the moment they are thrown away, and
	/// the moment they are shown.
	/// </summary>
	private void RefreshHoleCards(string playerId, Vector2 facing, PokerLayoutSpec spec)
	{
		var revealed = _game.RevealedHoleCards.GetValueOrDefault(playerId);
		var hand = EnsureHand(playerId);
		if (hand == null)
			return;

		// A new hand throws a fresh pair. Staggered round the table, one card each and then the
		// second, the way a dealer actually does it.
		if (hand.Hand != _game.HandNumber)
		{
			ReleaseCardsFromGrip(hand);
			hand.Hand = _game.HandNumber;
			hand.OnTable = 0.0f;
			hand.Revealed = false;
			hand.Folded = false;
			hand.Mucked = 0.0f;
			hand.Returning = false;
			hand.Returned = 1.0f;

			var seat = System.Array.IndexOf(_game.SeatOrder, playerId);
			var seats = Mathf.Max(1, _game.SeatOrder.Length);

			for (var i = 0; i < hand.Cards.Length; i++)
			{
				hand.Dealt[i] = 0.0f;
				hand.Wait[i] = (i * seats + Mathf.Max(seat, 0)) * DealStagger;
				hand.ReleasedFrom[i] = Transform3D.Identity;
				hand.Cards[i].Configure(0, spec, faceDown: true);
			}
		}

		// Giving up is a THROW, not a pair quietly disappearing. The cards go into the middle and
		// stay there, face down and out of true, for the rest of the hand — which is what tells the
		// rest of the table that this seat is out, and the one thing a fold has to say out loud.
		if (revealed == null && _game.HasFolded(playerId) && !hand.Folded)
		{
			hand.Folded = true;
			hand.Mucked = 0.0f;
			hand.FromHands = HasTakenCardsUp(playerId, hand);
			ReleaseCardsFromGrip(hand);
		}

		if (revealed != null && !hand.Revealed)
		{
			var wasHeldLocally = hand.InFirstPerson;
			ReleaseCardsFromGrip(hand);
			// A showdown puts them back on the cloth, face up and already in place.
			hand.Revealed = true;
			hand.Returning = wasHeldLocally;
			hand.Returned = wasHeldLocally ? 0.0f : 1.0f;
			for (var i = 0; i < hand.Cards.Length; i++)
			{
				hand.Dealt[i] = 1.0f;
				hand.Wait[i] = 0.0f;
			}
		}

		for (var i = 0; i < hand.Cards.Length; i++)
		{
			var card = hand.Cards[i];

			if (revealed != null && i < revealed.Length)
				card.Configure(revealed[i], spec);
			// Once the local pair is in the private grip, its owner has configured the real faces. Public
			// refreshes must not turn those same nodes back into anonymous table placeholders.
			else if (!hand.InFirstPerson && (card.CardId != 0 || !card.IsFaceDown))
				card.Configure(0, spec, faceDown: true);
		}

		PlaceHoleCards(playerId, hand, facing, spec, revealed != null);
	}

	private SeatHand EnsureHand(string playerId)
	{
		if (_holeCards.TryGetValue(playerId, out var existing))
			return existing;

		var hand = new SeatHand();
		for (var i = 0; i < hand.Cards.Length; i++)
		{
			var card = _game?.CreateCard(CardScene);
			if (card == null)
				return null;

			AddChild(card);
			hand.Cards[i] = card;
		}

		_holeCards[playerId] = hand;
		return hand;
	}

	/// <summary>
	/// Moves the local player's actual dealt cards into the camera grip. No copy is created and no
	/// table card is hidden in exchange for a first-person replacement.
	/// </summary>
	public IReadOnlyList<PokerCard> TakeLocalCards(Node3D grip)
	{
		var playerId = _game?.Player == null ? null : (string)_game.Player.Name;
		if (string.IsNullOrEmpty(playerId) || grip == null)
			return System.Array.Empty<PokerCard>();

		var hand = EnsureHand(playerId);
		if (hand == null)
			return System.Array.Empty<PokerCard>();

		if (!hand.InFirstPerson)
		{
			foreach (var card in hand.Cards)
			{
				if (card == null || card.GetParent() == grip)
					continue;

				card.Reparent(grip, keepGlobalTransform: true);
				card.Visible = true;
			}

			hand.InFirstPerson = true;
		}

		return hand.Cards;
	}

	/// <summary>Returns held card nodes to the table while preserving their exact world transforms.</summary>
	public void ReleaseLocalCardsFromGrip()
	{
		var playerId = _game?.Player == null ? null : (string)_game.Player.Name;
		if (string.IsNullOrEmpty(playerId) || !_holeCards.TryGetValue(playerId, out var hand))
			return;

		ReleaseCardsFromGrip(hand);
	}

	private void ReleaseCardsFromGrip(SeatHand hand)
	{
		if (!hand.InFirstPerson)
			return;

		for (var i = 0; i < hand.Cards.Length; i++)
		{
			var card = hand.Cards[i];
			if (card == null)
				continue;

			if (card.GetParent() != this)
				card.Reparent(this, keepGlobalTransform: true);

			hand.ReleasedFrom[i] = card.Transform;
		}

		hand.InFirstPerson = false;
	}

	/// <summary>
	/// Where the pair is right now: somewhere between the deck and the seat while it is being
	/// thrown, and on the cloth once it lands.
	/// </summary>
	private void PlaceHoleCards(
		string playerId, SeatHand hand, Vector2 facing, PokerLayoutSpec spec, bool shown)
	{
		// Turned toward whoever is LOOKING, not toward whose cards they are. A showdown exists to be
		// read, and a hand laid out for its owner is upside down to everyone it was shown to.
		var yaw = ReaderYaw(facing);
		var turned = Basis.FromEuler(new Vector3(0.0f, yaw, 0.0f));
		var deck = BoardPresenter.DeckPosition;

		if ((_showdownPresenter?.Active ?? false) && shown)
		{
			foreach (var source in hand.Cards)
				source.Visible = false;
			return;
		}

		if (_game.HandNumber > 0 && hand.Folded && !shown)
		{
			PlaceMuck(playerId, hand, facing, spec, yaw);
			return;
		}

		var taken = !shown && HasTakenCardsUp(playerId, hand);
		if (hand.InFirstPerson)
			return;

		for (var i = 0; i < hand.Cards.Length; i++)
		{
			var card = hand.Cards[i];

			if (_game.HandNumber <= 0 || taken)
			{
				card.Visible = false;
				continue;
			}

			card.Visible = true;

			var place = PokerTableLayout.SeatCardPosition(facing, i, spec);
			var seated = new Vector3(place.X, spec.CardThickness * 0.5f, place.Y);

			if (shown && hand.Returning)
			{
				var delay = i * 0.10f;
				var returned = PokerMotion.Smooth(Mathf.Clamp(
					(hand.Returned - delay) / (1.0f - delay), 0.0f, 1.0f));
				var from = hand.ReleasedFrom[i].Origin;
				var returnPosition = PokerMotion.CardThrow(from, seated, returned, 0.035f,
					PokerChipPile.Noise(i, 18) * 0.010f);
				var target = turned * PokerCard.Orientation(false);
				var basis = hand.ReleasedFrom[i]
					.InterpolateWith(new Transform3D(target, seated), returned)
					.Basis;
				card.Transform = new Transform3D(basis, returnPosition);
				continue;
			}

			var seat = Mathf.Max(System.Array.IndexOf(_game.SeatOrder, playerId), 0);
			var motionSeed = seat * PokerDeal.HoleCardCount + i;
			var position = PokerMotion.CardThrow(
				deck, seated, hand.Dealt[i], DealArc,
				PokerChipPile.Noise(motionSeed, 10) * 0.012f);
			var airborne = 1.0f - PokerMotion.Smooth(hand.Dealt[i]);
			var dealWobble = new Basis(Vector3.Up,
					PokerChipPile.Noise(motionSeed, 11) * 0.22f * airborne)
				* new Basis(Vector3.Forward,
					PokerChipPile.Noise(motionSeed, 12) * 0.10f * airborne);

			// Configure only RECORDS that a card is face down; turning it over is the caller's job.
			// Without this the pair lay face up showing a blank white placeholder.
			card.Transform = new Transform3D(turned * dealWobble * PokerCard.Orientation(!shown), position);
		}
	}

	/// <summary>
	/// Whether this seat's pair is off the cloth and in its owner's hands.
	///
	/// Known for certain about the local player and guessed for everyone else — an opponent's own
	/// pick-up is a purely local gesture on their machine and nothing about it is broadcast, so
	/// every peer plays the same beat on the same clock instead.
	/// </summary>
	private bool HasTakenCardsUp(string playerId, SeatHand hand) =>
		_game.Player != null && playerId == (string)_game.Player.Name
			? _game.LocalPickedUpCards
			: hand.OnTable > OpponentPickUpDelay;

	/// <summary>
	/// The pair on its way into the muck, and lying in it afterwards.
	///
	/// Where it lands is derived from the SEAT rather than rolled, so every peer piles the discards
	/// identically and one thrown hand never lands squarely on another. Nothing about this is sent:
	/// the fold itself is already public state, and the throw follows from it.
	/// </summary>
	private void PlaceMuck(string playerId, SeatHand hand, Vector2 facing, PokerLayoutSpec spec, float yaw)
	{
		var muck = BoardPresenter.MuckPosition;
		var seat = Mathf.Max(System.Array.IndexOf(_game.SeatOrder, playerId), 0);

		for (var i = 0; i < hand.Cards.Length; i++)
		{
			var card = hand.Cards[i];
			card.Visible = true;

			var slot = seat * hand.Cards.Length + i;
			var delay = i * 0.10f;
			var settled = PokerMotion.Smooth(Mathf.Clamp(
				(hand.Mucked - delay) / (1.0f - delay), 0.0f, 1.0f));

			var rest = muck + new Vector3(
				PokerChipPile.Noise(slot, 0) * MuckSpread,
				(slot + 0.5f) * spec.CardThickness * 1.6f,
				PokerChipPile.Noise(slot, 1) * MuckSpread);

			var from = MuckSource(hand, facing, spec, i);

			var position = PokerMotion.CardThrow(
				from, rest, settled, MuckArc,
				PokerChipPile.Noise(slot, 5) * 0.018f);

			// It turns as it goes. A hand thrown in flat and square reads as a hand being dealt
			// backwards; a discard lands askew.
			var spun = Mathf.LerpAngle(yaw, yaw + PokerChipPile.Noise(slot, 2) * Mathf.Pi, settled);
			var flutter = Mathf.Sin(settled * Mathf.Pi) * PokerChipPile.Noise(slot, 6) * 0.22f;
			var targetBasis = new Basis(Vector3.Up, spun) * new Basis(Vector3.Forward, flutter)
				* PokerCard.Orientation(true);
			var basis = targetBasis;

			if (hand.FromHands && !hand.ReleasedFrom[i].Origin.IsZeroApprox())
			{
				basis = hand.ReleasedFrom[i]
					.InterpolateWith(new Transform3D(targetBasis, rest), settled)
					.Basis;
			}

			card.Transform = new Transform3D(basis, position);
		}
	}

	/// <summary>Where a thrown pair sets off from: the player's own hands, or the cloth.</summary>
	private Vector3 MuckSource(SeatHand hand, Vector2 facing, PokerLayoutSpec spec, int index)
	{
		if (!hand.FromHands)
		{
			var laid = PokerTableLayout.SeatCardPosition(facing, index, spec);
			return new Vector3(laid.X, spec.CardThickness * 0.5f, laid.Y);
		}

		// The local peer owns the actual first-person nodes. Once they are reparented here, this is
		// their exact release point rather than a second copy appearing at an estimated hand pose.
		if (index >= 0 && index < hand.ReleasedFrom.Length
			&& !hand.ReleasedFrom[index].Origin.IsZeroApprox())
			return hand.ReleasedFrom[index].Origin;

		// Remote peers do not own the first-person grip, so they use the shared seated estimate until
		// a third-person hand rig supplies a physical release marker.
		var held = facing * (spec.SeatCardRadius - 0.05f);
		var across = new Vector2(-facing.Y, facing.X) * (index == 0 ? -0.03f : 0.03f);

		return new Vector3(held.X + across.X, HandHeight, held.Y + across.Y);
	}

	public override void _Process(double delta)
	{
		if (_game == null)
			return;

		// Taking the cards up is a purely local gesture and emits no state signal, so nothing else
		// would ever tell this presenter to stop drawing the pair lying on the cloth.
		var moved = _game.LocalPickedUpCards != _lastPickedUp;
		_lastPickedUp = _game.LocalPickedUpCards;

		var localId = _game.Player == null ? null : (string)_game.Player.Name;
		LocalHandLanded = false;

		foreach (var entry in _holeCards)
		{
			var hand = entry.Value;
			var landed = true;

			if (hand.Returning && hand.Returned < 1.0f)
			{
				hand.Returned = Mathf.Min(1.0f,
					hand.Returned + (float)delta / Mathf.Max(MuckSeconds, 0.01f));
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

	/// <summary>
	/// Replays what somebody just did, on their own seated body.
	///
	/// Derived from the turn context every peer already holds — the action code and who made it are
	/// both in there — so a gesture costs NO message at all and every peer arrives at the same table
	/// on its own. The turn stamp is the change key, because it moves exactly once per action.
	///
	/// The body clips are not authored yet, so today this resolves to the seated idle everywhere.
	/// The call site is the point: when they exist, they appear here and nowhere else.
	/// </summary>
	private void PlayActionGesture()
	{
		if (_game.ActionSeq == _lastGestureToken)
			return;

		_lastGestureToken = _game.ActionSeq;

		var gesture = PokerClips.ForAction(_game.LastAction);
		if (gesture == PokerGesture.None || string.IsNullOrEmpty(_game.LastPlayer))
			return;

		if (PlayerRegistry.Instance is { } registry && registry.HasContainer())
			registry.GetPlayerById(_game.LastPlayer)?.PlaySeatedGesture(PokerClips.ThirdPerson(gesture));

		if (gesture == PokerGesture.Knock)
			PlayKnock(_game.LastPlayer);
	}

	/// <summary>A knuckle on the table. Heard by everyone, positioned at the seat that made it.</summary>
	private void PlayKnock(string playerId)
	{
		if (KnockSound == null)
			return;

		_knock ??= NewKnockPlayer();

		var seat = SeatNodeFor(playerId);
		if (seat != null)
			_knock.GlobalPosition = seat.GlobalPosition with { Y = GlobalPosition.Y };

		_knock.Play();
	}

	private AudioStreamPlayer3D NewKnockPlayer()
	{
		var player = new AudioStreamPlayer3D
		{
			Stream = KnockSound,
			UnitSize = 3.0f,
			MaxDistance = 12.0f,
		};

		AddChild(player);
		return player;
	}

	/// <summary>
	/// A seat's chips: what they have pushed in, and what they still hold.
	///
	/// Both piles are TURNED to face the seat. Left axis-aligned they spread along the table's own
	/// X wherever the player was sitting, which read as chips scattered at random rather than as two
	/// tidy rows in front of somebody.
	/// </summary>
	private void RefreshChips(string playerId, Vector2 facing, PokerLayoutSpec spec)
	{
		var stack = PileFor(_stacks, playerId, StackScatter, settles: false);
		if (stack == null)
			return;

		var turned = Basis.FromEuler(new Vector3(0.0f, PokerTableLayout.YawTowardCentre(facing), 0.0f));
		var stackPlace = StackPlace(facing, spec);

		stack.Transform = new Transform3D(turned, new Vector3(stackPlace.X, 0.0f, stackPlace.Y));
		if (_bankRuns.TryGetValue(playerId, out var bank))
			stack.SetRuns(bank);
		else
			stack.Show(_displayStacks.GetValueOrDefault(playerId, _game.StackOf(playerId)));
	}

	private Vector2 StackPlace(Vector2 facing, PokerLayoutSpec spec)
	{
		var direction = facing.Normalized();
		var across = new Vector2(-direction.Y, direction.X);
		return direction * Mathf.Max(spec.SeatBetRadius + 0.08f, spec.SeatStackRadius - StackInset)
			+ across * StackSideOffset;
	}

	/// <summary>
	/// Captures immutable presentation events before the next aggregate context can erase them.
	/// A call that closes a street therefore remains queued after BetThisRound has returned to zero.
	/// </summary>
	private void CaptureChipPresentation(PokerLayoutSpec spec)
	{
		if (_game.HandNumber <= 0)
			return;

		// A new match also starts at hand 1. The action sequence moving backwards distinguishes that
		// restart from another refresh of the same hand and prevents the old pot/street from leaking in.
		if (_presentationHand != _game.HandNumber || _game.ActionSeq < _presentationActionSeq)
		{
			SnapToAuthoritativeState(spec);
			return;
		}

		_requestedStreet = (PokerStreet)Mathf.Max((int)_requestedStreet, (int)_game.Street);
		if (_requestedStreet > _visibleStreet || (_game.HandSettled && !_settlementCollected))
			_collectionRequested = true;

		if (_presentationActionSeq == _game.ActionSeq)
			return;

		_presentationActionSeq = _game.ActionSeq;
		var playerId = _game.LastPlayer;
		if (!string.IsNullOrEmpty(playerId) && _game.LastAction is "call" or "raise")
		{
			var before = _observedStacks.GetValueOrDefault(playerId, _game.StackOf(playerId));
			var now = _game.StackOf(playerId);
			var committedBefore = _observedCommitted.GetValueOrDefault(playerId);
			var committedNow = _game.BetThisHand.GetValueOrDefault(playerId);
			var amount = Mathf.Max(before - now, committedNow - committedBefore);

			if (amount > 0)
				_pendingChipActions.Enqueue(new PendingChipAction(
					playerId, amount, _game.Street, now));
			else
				_displayStacks[playerId] = now;
		}

		RememberPublicChipState();
	}

	/// <summary>Public recovery hook for a peer that has just received a fresh authoritative snapshot.</summary>
	public void SnapToAuthoritativeState()
	{
		if (_game != null && BoardPresenter != null)
			SnapToAuthoritativeState(BoardPresenter.Spec);
	}

	private void SnapToAuthoritativeState(PokerLayoutSpec spec)
	{
		LastRecoveryDiscardedAnimation = _presentationHand >= 0
			&& (_pendingChipActions.Count > 0 || _collecting || _organizing || _collectionRequested
				|| HasPhase(ChipBatchPhase.ToBet, ChipBatchPhase.Landing, ChipBatchPhase.ToPot,
					ChipBatchPhase.Organizing, ChipBatchPhase.ToDealer, ChipBatchPhase.ToWinner)
				|| ((_showdownPresenter?.Active ?? false) && !(_showdownPresenter?.ReadyForPayout ?? true)));
		_showdownPresenter?.Reset();
		_presentationHand = _game.HandNumber;
		_presentationActionSeq = _game.ActionSeq;
		_visibleStreet = _game.Street;
		_requestedStreet = _game.Street;
		_collecting = false;
		_organizing = false;
		_collectionRequested = false;
		_settlementCollected = _game.HandSettled;
		_payoutSequencer?.Reset(_game.HandSettled);
		_pendingChipActions.Clear();
		_observedStacks.Clear();
		_observedCommitted.Clear();
		_displayStacks.Clear();
		_bankRuns.Clear();
		_nextChipSequence = 0;

		_chipAnimator?.ResetAll();

		BoardPresenter.EnablePresentationGate(_visibleStreet);

		foreach (var playerId in _game.SeatOrder)
		{
			_displayStacks[playerId] = _game.StackOf(playerId);
			_bankRuns[playerId] = PokerChipStack.CreatePlayableBank(_game.StackOf(playerId));
			var blind = _game.BetOf(playerId);
			if (blind > 0)
				PlaceInitialBet(playerId, blind, spec);
		}

		// PotInMiddle is already authoritative on a late join. Replaying historical calls would be
		// both impossible and visually misleading, so reconstruct the same denomination columns now.
		if (!_game.HandSettled && _game.PotInMiddle > 0)
			PlaceOrganizedPotSnapshot(_game.PotInMiddle);

		if (_game.HandSettled)
			_showdownPresenter?.Reset(authoritativeSettled: true, hand: _game.HandNumber);

		RememberPublicChipState();
	}

	private void RememberPublicChipState()
	{
		foreach (var playerId in _game.SeatOrder)
		{
			_observedStacks[playerId] = _game.StackOf(playerId);
			_observedCommitted[playerId] = _game.BetThisHand.GetValueOrDefault(playerId);
		}
	}

	/// <summary>True when a newly offered action will not overtake an unfinished table animation.</summary>
	public bool PresentationReadyForAction =>
		_pendingChipActions.Count == 0
		&& !_collecting
		&& !_organizing
		&& !_collectionRequested
		&& !HasPhase(ChipBatchPhase.ToBet, ChipBatchPhase.Landing, ChipBatchPhase.ToPot,
			ChipBatchPhase.Organizing, ChipBatchPhase.ToDealer, ChipBatchPhase.ToWinner)
		&& _visibleStreet >= _requestedStreet
		&& (BoardPresenter?.Settled ?? true);

	/// <summary>
	/// Instance ids of the physical chip visuals currently representing bets or the pot. Primarily a
	/// regression aid: their identity must survive landing, collection and organization.
	/// </summary>
	public IReadOnlyList<ulong> ActiveChipVisualIds()
		=> _chipAnimator?.ActiveVisualIds() ?? System.Array.Empty<ulong>();

	/// <summary>Current table-local positions keyed by persistent chip instance.</summary>
	public IReadOnlyDictionary<ulong, Vector3> ActiveChipVisualPositions()
		=> _chipAnimator?.ActiveVisualPositions()
			?? new Dictionary<ulong, Vector3>();

	/// <summary>Regression guard for the first rendered frame of a call or raise.</summary>
	public bool NewlyStartedBatchesAreAtTheirOrigin => _chipAnimator.Batches.All(batch =>
		batch.Phase != ChipBatchPhase.ToBet || batch.Progress > 0.0f
		|| batch.Pile.Position.DistanceTo(batch.From) < 0.0001f);

	/// <summary>Persistent bank visuals used to prove a payment only removes chips.</summary>
	public IReadOnlyDictionary<ulong, Transform3D> StackChipTransforms(string playerId)
	{
		var transforms = new Dictionary<ulong, Transform3D>();
		if (!_stacks.TryGetValue(playerId, out var pile))
			return transforms;

		foreach (var child in pile.GetChildren())
		{
			if (child is Node3D { Visible: true } visual)
				transforms[visual.GetInstanceId()] = visual.Transform;
		}
		return transforms;
	}

	public IReadOnlyDictionary<ulong, Vector3> StackChipPositions(string playerId)
	{
		var positions = new Dictionary<ulong, Vector3>();
		if (!_stacks.TryGetValue(playerId, out var pile))
			return positions;

		foreach (var child in pile.GetChildren())
		{
			if (child is Node3D { Visible: true } visual)
				positions[visual.GetInstanceId()] = pile.Transform * visual.Position;
		}
		return positions;
	}

	public bool BetsAreVisuallyLoose => _chipAnimator.Batches.Any(batch =>
		batch.Phase == ChipBatchPhase.AtBet && batch.Pile.Spread > 0.95f);

	public bool PotIsLooseWhileOrganizing => _chipAnimator.Batches.Any(batch =>
		batch.Phase == ChipBatchPhase.Organizing && batch.Progress < 0.25f
		&& batch.Pile.Spread > 0.70f);

	public bool PotIsOrganizedTower
	{
		get
		{
			var found = false;
			foreach (var batch in _chipAnimator.Batches)
			{
				if (batch.Phase != ChipBatchPhase.InPot)
					continue;
				found = true;
				if (batch.Pile.Spread > 0.001f)
					return false;
			}
			return found;
		}
	}

	/// <summary>Number of denomination columns currently visible in the organized pot.</summary>
	public int PotDenominationColumnCount => _chipAnimator.Batches
		.Where(batch => batch.Phase == ChipBatchPhase.InPot)
		.Select(DenominationOf).Where(value => value > 0).Distinct().Count();

	public bool PayoutStarted => _payoutSequencer?.Started ?? false;
	public bool PayoutCompleted => _payoutSequencer?.Completed ?? false;
	public bool DealerChangeInProgress => _payoutSequencer?.DealerChangeInProgress ?? false;
	public bool DealerChangeCompleted => _payoutSequencer?.DealerChangeCompleted ?? false;
	public int ChipsDeliveredToWinners => _chipAnimator.Batches.Count(batch =>
		batch.Phase == ChipBatchPhase.AtWinner);
	public int PayoutRecipientCount => _chipAnimator.Batches
		.Where(batch => batch.Phase == ChipBatchPhase.AtWinner)
		.Select(batch => batch.WinnerId).Where(id => !string.IsNullOrEmpty(id)).Distinct().Count();
	public int ActiveChipGroupCount => _chipAnimator?.ActiveBatchCount ?? 0;
	public int PayoutPhysicalValueOf(string playerId) => _chipAnimator?.Batches
		.Where(batch => batch.Phase == ChipBatchPhase.AtWinner && batch.WinnerId == playerId)
		.Sum(batch => batch.Amount) ?? 0;
	public IReadOnlyList<int> PotPhysicalGroupValues() => _chipAnimator?.Batches
		.Where(batch => batch.Phase == ChipBatchPhase.InPot)
		.OrderByDescending(batch => batch.Amount).ThenBy(batch => batch.Sequence)
		.Select(batch => batch.Amount).ToList() ?? new List<int>();

	/// <summary>
	/// Stack value currently represented on the cloth. This deliberately trails the authoritative
	/// value until the corresponding physical batch starts moving, keeping removal and take-off in
	/// the same rendered frame.
	/// </summary>
	public int DisplayedStackOf(string playerId) =>
		_displayStacks.GetValueOrDefault(playerId, _game?.StackOf(playerId) ?? 0);

	private void BuildChipBatchPool()
	{
		_chipAnimator = GetNodeOrNull<PokerChipAnimator>("ChipAnimator");
		if (_chipAnimator == null)
		{
			_chipAnimator = new PokerChipAnimator { Name = "ChipAnimator" };
			AddChild(_chipAnimator);
		}

		_chipAnimator.Configure(_game?.ChipScene, ChipScatter,
			ChipBatchPoolSize, PrewarmedChipsPerBatch,
			ChipFlightSeconds, ChipFlightArc, ChipLandingSeconds,
			ChipCollectSeconds, ChipOrganizeSeconds, ChipPayoutSeconds,
			Profile.DealerChangeSeconds);
	}

	private void BuildPresentationComponents()
	{
		_showdownPresenter = GetNodeOrNull<PokerShowdownPresenter>("ShowdownPresenter");
		if (_showdownPresenter == null)
		{
			_showdownPresenter = new PokerShowdownPresenter { Name = "ShowdownPresenter" };
			AddChild(_showdownPresenter);
		}
		_showdownPresenter.Configure(_game, BoardPresenter, CardScene, Profile,
			ShowdownSourceTransform, NameOf, HideShowdownSources,
			ShowdownRowSpacing, ShowdownCardSpacing, ShowdownArc,
			ShowdownWinnerColor, ShowdownOtherColor);

		_payoutSequencer = GetNodeOrNull<PokerPayoutSequencer>("PayoutSequencer");
		if (_payoutSequencer == null)
		{
			_payoutSequencer = new PokerPayoutSequencer { Name = "PayoutSequencer" };
			AddChild(_payoutSequencer);
		}
		_payoutSequencer.Configure(_chipAnimator, Profile, MaxAnimatedChipGroups,
			AcquireBatch, NextChipSequence, TryPayoutSeatPlaces, WinnerStackOffset);
		SignalUtil.ConnectGuarded(_payoutSequencer,
			PokerPayoutSequencer.SignalName.DealerChangeStarted,
			new Callable(this, MethodName.OnDealerChangeStarted));
	}

	private void OnDealerChangeStarted()
	{
		if (DealerAnimator != null && !string.IsNullOrWhiteSpace(DealerChangeAnimation)
			&& DealerAnimator.HasAnimation(DealerChangeAnimation))
			DealerAnimator.Play(DealerChangeAnimation);

		if (DealerChangeSound == null)
			return;
		_dealerChangeAudio ??= new AudioStreamPlayer3D
		{
			Name = "DealerChangeAudio",
			UnitSize = 2.0f,
			MaxDistance = 10.0f,
		};
		if (_dealerChangeAudio.GetParent() == null)
			AddChild(_dealerChangeAudio);
		_dealerChangeAudio.Position = BoardPresenter.DeckPosition;
		_dealerChangeAudio.Stream = DealerChangeSound;
		_dealerChangeAudio.Play();
	}

	private int NextChipSequence() => _nextChipSequence++;

	private bool TryPayoutSeatPlaces(string playerId, out Basis basis, out Vector3 stack)
		=> TrySeatChipPlaces(playerId, BoardPresenter.Spec, out basis, out stack, out _);

	private void HideShowdownSources(IReadOnlyList<string> players)
	{
		for (var index = 0; index < PokerDeal.BoardCount; index++)
		{
			var source = BoardPresenter.BoardCardNodeAt(index);
			if (source != null)
				source.Visible = false;
		}
		foreach (var playerId in players)
		{
			if (!_holeCards.TryGetValue(playerId, out var hand))
				continue;
			foreach (var source in hand.Cards)
				source.Visible = false;
		}
	}

	private ChipBatch AcquireBatch() => _chipAnimator.Acquire();

	private void PlaceInitialBet(string playerId, int amount, PokerLayoutSpec spec)
	{
		if (!TrySeatChipPlaces(playerId, spec, out var basis, out _, out var bet))
			return;

		var available = Mathf.Max(1, MaxAnimatedChipGroups - _chipAnimator.ActiveBatchCount);
		foreach (var run in PokerChipAnimator.GroupRuns(PokerChipStack.Decompose(amount), available))
		{
			var batch = AcquireBatch();
			batch.PlayerId = playerId;
			batch.Amount = run.Value;
			batch.Basis = basis;
			batch.Sequence = _nextChipSequence++;
			batch.Phase = ChipBatchPhase.AtBet;
			batch.To = bet;
			batch.Pile.Visible = false;
			batch.Pile.Transform = new Transform3D(basis, batch.To);
			batch.Pile.LooseSlotOffset = NextBetLooseSlot(playerId);
			batch.Pile.Spread = 1.0f;
			batch.Pile.FlightProgress = 1.0f;
			batch.Pile.SetRuns(new[] { run });
			batch.Pile.Visible = true;
		}
	}

	private void PlaceOrganizedPotSnapshot(int amount)
	{
		var centre = BoardPresenter.PotPosition;
		var available = Mathf.Max(1, MaxAnimatedChipGroups - _chipAnimator.ActiveBatchCount);
		foreach (var run in PokerChipAnimator.GroupRuns(PokerChipStack.Decompose(amount), available))
		{
			var batch = AcquireBatch();
			batch.Amount = run.Value;
			batch.Sequence = _nextChipSequence++;
			batch.Basis = Basis.Identity;
			batch.From = centre;
			batch.To = centre;
			batch.Phase = ChipBatchPhase.AtPotLoose;
			batch.Pile.Visible = false;
			batch.Pile.Transform = new Transform3D(Basis.Identity, centre);
			batch.Pile.Spread = 1.0f;
			batch.Pile.FlightProgress = 1.0f;
			batch.Pile.SetRuns(new[] { run });
			batch.Pile.Visible = true;
		}

		BeginOrganization();
		foreach (var batch in _chipAnimator.Batches)
		{
			if (batch.Phase != ChipBatchPhase.Organizing)
				continue;
			batch.Pile.Transform = new Transform3D(batch.ToBasis, batch.OrganizeTo);
			batch.Pile.Spread = 0.0f;
			batch.Progress = 1.0f;
			batch.Phase = ChipBatchPhase.InPot;
		}
		_organizing = false;
	}

	private bool StartChipAction(PendingChipAction action)
	{
		// Capture and playback happen on different callbacks. Updating the aggregate pile while the
		// action signal is being captured used to leave a rendered frame in which chips vanished from
		// the stack before this batch appeared -- the small "flick" visible at the start of a call.
		// Commit both visual changes here so the departing chips replace the removed value atomically.
		_displayStacks[action.PlayerId] = action.StackAfter;

		if (!TrySeatChipPlaces(action.PlayerId, BoardPresenter.Spec,
				out var basis, out var stack, out var bet))
			return false;

		var payment = TakeVisualPayment(action.PlayerId, action.Amount, action.StackAfter);
		if (_stacks.TryGetValue(action.PlayerId, out var bankPile)
			&& _bankRuns.TryGetValue(action.PlayerId, out var bank))
			bankPile.SetRuns(bank);

		var groupIndex = 0;
		var paidByDenomination = new Dictionary<int, int>();
		var available = Mathf.Max(1, MaxAnimatedChipGroups - _chipAnimator.ActiveBatchCount);
		foreach (var run in PokerChipAnimator.GroupRuns(payment, available))
		{
			var batch = AcquireBatch();
			batch.PlayerId = action.PlayerId;
			batch.Amount = run.Value;
			batch.Basis = basis;
			batch.Sequence = _nextChipSequence++;
			batch.To = bet;
			batch.Progress = 0.0f;
			batch.Delay = groupIndex++ * Profile.ChipFlightStagger;
			batch.Phase = ChipBatchPhase.ToBet;
			var paidBefore = paidByDenomination.GetValueOrDefault(run.Denomination);
			paidByDenomination[run.Denomination] = paidBefore + run.Count;
			var departure = _bankRuns.TryGetValue(action.PlayerId, out var bankAfter)
				? PaymentDepartureOffset(bankAfter, run.Denomination, paidBefore, bankPile)
				: Vector3.Up * (bankPile?.TopHeight ?? 0.0f);
			batch.From = stack + basis * departure;

			// Configure while hidden and reveal at the real physical source. A large stack may travel
			// as a compact same-denomination group, but it never changes value or chip type in flight.
			batch.Pile.Visible = false;
			batch.Pile.Transform = new Transform3D(basis, batch.From);
			batch.Pile.LooseSlotOffset = NextBetLooseSlot(action.PlayerId);
			batch.Pile.Spread = 0.0f;
			batch.Pile.FlightProgress = 0.0f;
			batch.Pile.SetRuns(new[] { run });
			batch.JustStarted = true;
			batch.Pile.Visible = true;
		}
		return groupIndex > 0;
	}

	private int NextBetLooseSlot(string playerId)
	{
		var next = 0;
		foreach (var batch in _chipAnimator.Batches)
		{
			if (batch.PlayerId != playerId || batch.Phase is not
				(ChipBatchPhase.ToBet or ChipBatchPhase.Landing or ChipBatchPhase.AtBet))
				continue;
			next += batch.Pile.ChipCount;
		}
		return next;
	}

	private List<ChipRun> TakeVisualPayment(string playerId, int amount, int stackAfter)
	{
		if (!_bankRuns.TryGetValue(playerId, out var bank))
		{
			bank = PokerChipStack.CreatePlayableBank(stackAfter + amount);
			_bankRuns[playerId] = bank;
		}

		if (PokerChipStack.TryTake(bank, amount, out var exact))
			return exact;

		// Custom integer raises can exhaust a denomination that normal 5-chip betting always has.
		// Never rebuild the bank during the action: remove a similar number of top visuals only, while
		// the exact batch still communicates the paid value. The HUD remains the monetary authority.
		var payment = PokerChipStack.Decompose(amount, 4);
		var toRemove = PokerChipStack.ChipCount(payment);
		for (var lane = bank.Count - 1; lane >= 0 && toRemove > 0; lane--)
		{
			var run = bank[lane];
			var removed = Mathf.Min(run.Count, toRemove);
			bank[lane] = new ChipRun(run.Denomination, run.Count - removed);
			toRemove -= removed;
		}

		return payment;
	}

	private static Vector3 PaymentDepartureOffset(
		IReadOnlyList<ChipRun> bankAfter, int denomination, int paidChip,
		PokerChipPile bankPile)
	{
		if (bankPile == null)
			return Vector3.Zero;

		for (var lane = 0; lane < bankAfter.Count; lane++)
		{
			var after = bankAfter[lane];
			if (after.Denomination != denomination)
				continue;

			var x = (lane - (bankAfter.Count - 1) * 0.5f) * bankPile.StackSpacing;
			// The batch's own single chip is centred half a thickness above its root.
			return new Vector3(x,
				(after.Count + paidChip) * bankPile.EffectiveThickness, 0.0f);
		}

		return Vector3.Up * Mathf.Max(0.0f,
			bankPile.TopHeight - bankPile.EffectiveThickness * 0.5f);
	}

	private bool TrySeatChipPlaces(
		string playerId, PokerLayoutSpec spec, out Basis basis, out Vector3 stack, out Vector3 bet)
	{
		basis = Basis.Identity;
		stack = Vector3.Zero;
		bet = Vector3.Zero;

		var seat = SeatNodeFor(playerId);
		if (seat == null)
			return false;

		var toSeat = ToLocal(seat.GlobalPosition);
		var facing = new Vector2(toSeat.X, toSeat.Z);
		if (facing.LengthSquared() < 1e-6f)
			return false;

		facing = facing.Normalized();
		basis = Basis.FromEuler(new Vector3(0.0f, PokerTableLayout.YawTowardCentre(facing), 0.0f));
		var stack2 = StackPlace(facing, spec);
		var bet2 = PokerTableLayout.SeatSpot(facing, spec.SeatBetRadius);
		stack = new Vector3(stack2.X, 0.0f, stack2.Y);
		bet = new Vector3(bet2.X, 0.0f, bet2.Y);
		return true;
	}

	private bool AdvanceChipPresentation(float delta)
	{
		var moved = false;

		// An action from the visible street goes first, even when it is the call that requested the
		// following street. Actions already received for a future street wait behind collection.
		if (!_collecting && !HasPhase(ChipBatchPhase.ToBet, ChipBatchPhase.Landing)
			&& _pendingChipActions.TryPeek(out var next) && next.Street <= _visibleStreet)
		{
			_pendingChipActions.Dequeue();
			moved |= StartChipAction(next);
		}

		foreach (var batch in _chipAnimator.Batches)
		{
			var phaseBefore = batch.Phase;
			moved |= _chipAnimator.Advance(batch, delta);
			if (phaseBefore == ChipBatchPhase.ToWinner
				&& batch.Phase == ChipBatchPhase.AtWinner)
			{
				_displayStacks[batch.WinnerId] = Mathf.Min(_game.StackOf(batch.WinnerId),
					_displayStacks.GetValueOrDefault(batch.WinnerId) + batch.Amount);
			}
		}

		var payoutWasCompleted = _payoutSequencer?.Completed ?? false;
		moved |= _payoutSequencer?.Advance() ?? false;

		var visibleActionPending = _pendingChipActions.TryPeek(out var queued)
			&& queued.Street <= _visibleStreet;
		if (_collectionRequested && !_collecting && !visibleActionPending
			&& !HasPhase(ChipBatchPhase.ToBet, ChipBatchPhase.Landing))
		{
			BeginCollection();
			moved = true;
		}

		if (_collecting && !_organizing && !HasPhase(ChipBatchPhase.ToPot))
		{
			BeginOrganization();
			moved = true;
		}

		if (_organizing && !HasPhase(ChipBatchPhase.Organizing))
		{
			_organizing = false;
			_collecting = false;
			_collectionRequested = false;
			_settlementCollected = _game.HandSettled;
			_visibleStreet = _requestedStreet;
			BoardPresenter.AllowBoardThrough(_visibleStreet);
			moved = true;
		}

		if (ShouldBeginPayout())
		{
			moved |= _payoutSequencer?.Begin(_game.Winners, _game.SeatOrder,
				BoardPresenter.DeckPosition + Vector3.Up * 0.006f) ?? false;
		}

		if (!payoutWasCompleted && (_payoutSequencer?.Completed ?? false))
		{
			foreach (var winner in _game.Winners.Keys)
				_displayStacks[winner] = _game.StackOf(winner);
			moved = true;
		}

		return moved;
	}

	private void BeginCollection()
	{
		_collecting = true;
		_organizing = false;
		var order = 0;
		var looseSlot = 0;
		var pot = BoardPresenter.PotPosition;
		foreach (var batch in _chipAnimator.Batches)
		{
			if (batch.Phase == ChipBatchPhase.InPot)
				looseSlot += batch.Pile.ChipCount;
		}

		foreach (var batch in _chipAnimator.Batches)
		{
			if (batch.Phase != ChipBatchPhase.AtBet)
				continue;

			batch.From = batch.Pile.Position;
			batch.To = pot;
			batch.Pile.RetargetLooseSlots(looseSlot);
			looseSlot += batch.Pile.ChipCount;
			batch.Progress = 0.0f;
			batch.Delay = order++ * ChipCollectStagger;
			batch.Phase = ChipBatchPhase.ToPot;
			batch.Pile.FlightProgress = 0.0f;
		}

		// A checked-through street has nothing new to sweep. Reorganizing an already tidy pot made the
		// table wait — and could add a tiny motion — before every community card for no physical reason.
		if (order == 0)
		{
			_collecting = false;
			_collectionRequested = false;
			_settlementCollected = _game.HandSettled;
			_visibleStreet = _requestedStreet;
			BoardPresenter.AllowBoardThrough(_visibleStreet);
		}
	}

	private void BeginOrganization()
	{
		_organizing = true;
		var reader = new Basis(Vector3.Up, ReaderYaw(Vector2.Down));
		_chipAnimator.BeginOrganization(BoardPresenter.PotPosition, reader, PotColumnSpacing);
	}

	private static int DenominationOf(ChipBatch batch) => PokerChipAnimator.DenominationOf(batch);

	private bool ShouldBeginPayout()
	{
		if ((_payoutSequencer?.Started ?? false) || (_payoutSequencer?.Completed ?? false)
			|| !_game.HandSettled || !_settlementCollected
			|| _collecting || _organizing || _collectionRequested
			|| HasPhase(ChipBatchPhase.ToBet, ChipBatchPhase.Landing,
				ChipBatchPhase.ToPot, ChipBatchPhase.Organizing, ChipBatchPhase.ToDealer))
			return false;

		// In an uncontested hand there is no comparison to wait for. At a showdown, however, the
		// chips only leave after everyone has read the revealed hands and the ranking is in place.
		if (_game.RevealedHoleCards.Count > 0 && !(_showdownPresenter?.ReadyForPayout ?? false))
			return false;

		return _game.Winners.Count > 0
			&& _chipAnimator.Batches.Any(batch => batch.Phase == ChipBatchPhase.InPot);
	}

	private Vector3 WinnerStackOffset(
		string winnerId, int denomination, int arrival, PokerChipPile movingPile)
	{
		if (!_bankRuns.TryGetValue(winnerId, out var bank) || bank.Count == 0)
			return Vector3.Up * arrival * movingPile.EffectiveThickness;

		var lane = bank.FindIndex(run => run.Denomination == denomination);
		var existing = lane >= 0 ? bank[lane].Count : 0;
		if (lane < 0)
			lane = bank.Count;
		var x = (lane - (bank.Count - 1) * 0.5f) * BankColumnSpacing;
		return new Vector3(x,
			(existing + arrival) * movingPile.EffectiveThickness, 0.0f);
	}

	private bool HasPhase(params ChipBatchPhase[] phases) =>
		_chipAnimator?.HasPhase(phases) ?? false;

	/// <summary>Whether the ranked five-card rows have finished reaching their comparison layout.</summary>
	public bool ShowdownPresentationSettled => _showdownPresenter?.Settled ?? false;

	/// <summary>Elapsed portion of the face-up reading beat before ranking begins.</summary>
	public float ShowdownRevealHoldElapsed => _showdownPresenter?.RevealHoldElapsed ?? 0.0f;

	/// <summary>Players in the same best-to-worst order currently shown on the cloth.</summary>
	public IReadOnlyList<string> ShowdownDisplayOrder =>
		_showdownPresenter?.DisplayOrder ?? System.Array.Empty<string>();

	public int ShowdownDisplayedCardCount => _showdownPresenter?.DisplayedCardCount ?? 0;

	public string DebugShowdownState()
	{
		var returning = 0;
		var phases = new Dictionary<ChipBatchPhase, int>();
		foreach (var hand in _holeCards.Values)
		{
			if (hand.Returning)
				returning++;
		}
		foreach (var batch in _chipAnimator.Batches)
			phases[batch.Phase] = phases.GetValueOrDefault(batch.Phase) + 1;
		var moving = new List<string>();
		foreach (var batch in _chipAnimator.Batches)
		{
			if (batch.Phase is ChipBatchPhase.ToBet or ChipBatchPhase.Landing
				or ChipBatchPhase.ToPot or ChipBatchPhase.Organizing or ChipBatchPhase.ToDealer)
				moving.Add($"{batch.Phase}:{batch.Progress:F2}/{batch.Delay:F2}");
		}

		return $"active={_showdownPresenter?.Active} settled={_showdownPresenter?.Settled} "
			+ $"hand={_game?.HandNumber} result={_game?.HandSettled} "
			+ $"reveals={_game?.RevealedHoleCards.Count} board={BoardPresenter?.Settled} "
			+ $"pending={_pendingChipActions.Count} collect={_collecting}/{_organizing}/{_collectionRequested} "
			+ $"returning={returning} visible={_visibleStreet} requested={_requestedStreet} "
			+ $"phases={string.Join(",", phases)} moving={string.Join(",", moving)}";
	}

	private Transform3D ShowdownSourceTransform(string playerId, int cardId)
	{
		if (_holeCards.TryGetValue(playerId, out var hand))
		{
			foreach (var card in hand.Cards)
			{
				if (card != null && card.CardId == cardId)
					return GlobalTransform.AffineInverse() * card.GlobalTransform;
			}
		}

		var boardIndex = System.Array.IndexOf(_game.Board, cardId);
		var boardCard = BoardPresenter.BoardCardNodeAt(boardIndex);
		if (boardCard != null)
			return GlobalTransform.AffineInverse() * boardCard.GlobalTransform;

		return new Transform3D(PokerCard.Orientation(false), BoardPresenter.DeckPosition);
	}

	private void RefreshName(string playerId, Vector2 facing)
	{
		// Your own plate would hang in the middle of your own view, telling you your own name. You
		// know who you are; what you need to read is everyone else.
		if (_game.Player != null && playerId == (string)_game.Player.Name)
		{
			if (_names.TryGetValue(playerId, out var own))
				own.Hide();

			return;
		}

		if (!_names.TryGetValue(playerId, out var label))
		{
			label = new Label3D
			{
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				NoDepthTest = false,
				// Small: these sit barely a metre from a seated eye, and the first render came out
				// with a name taller than the table.
				PixelSize = 0.00022f,
				FontSize = 64,
				OutlineSize = 10,
			};

			AddChild(label);
			_names[playerId] = label;
		}

		label.Show();

		var isTurn = _game.IsMatchActive && _game.IsTurnOwner(playerId);
		var folded = _game.HasFolded(playerId);
		var place = PokerTableLayout.SeatSpot(facing, BoardPresenter.Spec.SeatStackRadius + 0.06f);

		// Name and count together: with no panel anywhere, this is the only place a stack is
		// written down, and it has to be legible from the chair opposite.
		var suffix = _game.IsAllIn(playerId) ? " · all-in" : folded ? " · fora" : "";
		// Rules credit the award immediately, but the table count follows the physical chips so the
		// number does not jump before the pot has visibly reached its owner.
		label.Text = $"{NameOf(playerId)}\n{DisplayedStackOf(playerId)}{suffix}";
		label.Modulate = folded ? FoldedColor : isTurn ? TurnColor : IdleColor;
		label.Position = new Vector3(place.X, NameHeight, place.Y);
	}

	private void RefreshDealerButton(PokerLayoutSpec spec)
	{
		if (_game.SeatOrder.Length == 0 || _game.ButtonSeat >= _game.SeatOrder.Length)
		{
			if (_dealerButton != null)
				_dealerButton.Visible = false;

			return;
		}

		_dealerButton ??= BuildDealerButton();

		var seat = SeatNodeFor(_game.SeatOrder[_game.ButtonSeat]);
		if (seat == null)
		{
			_dealerButton.Visible = false;
			return;
		}

		var toSeat = ToLocal(seat.GlobalPosition);
		var facing = new Vector2(toSeat.X, toSeat.Z);
		if (facing.LengthSquared() < 1e-6f)
			return;

		facing = facing.Normalized();

		// Beside the seat's own things rather than in front of them, so it never covers a card.
		var across = new Vector2(-facing.Y, facing.X);
		var place = facing * (spec.SeatBetRadius + 0.02f) + across * ButtonOffset;

		_dealerButton.Position = new Vector3(place.X, 0.004f, place.Y);
		_dealerButton.Visible = true;
	}

	private MeshInstance3D BuildDealerButton()
	{
		var button = new MeshInstance3D
		{
			Mesh = new CylinderMesh
			{
				TopRadius = ButtonRadius,
				BottomRadius = ButtonRadius,
				Height = 0.006f,
				RadialSegments = 20,
				Rings = 1,
			},
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.96f, 0.95f, 0.90f),
				Roughness = 0.5f,
			},
		};

		AddChild(button);

		var label = new Label3D
		{
			Text = "D",
			Billboard = BaseMaterial3D.BillboardModeEnum.Disabled,
			PixelSize = 0.0003f,
			FontSize = 64,
			OutlineSize = 0,
			Modulate = new Color(0.15f, 0.15f, 0.18f),
			Position = new Vector3(0.0f, 0.004f, 0.0f),
			Rotation = new Vector3(-Mathf.Pi * 0.5f, 0.0f, 0.0f),
		};

		button.AddChild(label);
		return button;
	}

	private PokerChipPile PileFor(
		Dictionary<string, PokerChipPile> into, string playerId, float scatter, bool settles)
	{
		if (into.TryGetValue(playerId, out var pile))
			return pile;

		pile = new PokerChipPile
		{
			ChipScene = _game?.ChipScene,
			Scatter = scatter,
			StableRunColumns = true,
			StackSpacing = BankColumnSpacing,
			Spread = 0.0f,
			SettleSeconds = settles ? 0.24f : 0.0f,
			SettleHeight = 0.018f,
		};

		AddChild(pile);
		into[playerId] = pile;

		return pile;
	}

	/// <summary>
	/// Which way up a card on the cloth is drawn, so it reads from THIS peer's chair. Falls back to
	/// the card's own seat when the local player has none — a spectator has nowhere to read from.
	/// </summary>
	private float ReaderYaw(Vector2 fallbackFacing)
	{
		var playerId = _game.Player == null ? null : (string)_game.Player.Name;
		var seat = playerId == null ? null : SeatNodeFor(playerId);

		if (seat == null)
			return PokerTableLayout.YawTowardCentre(fallbackFacing);

		var toSeat = ToLocal(seat.GlobalPosition);
		var reader = new Vector2(toSeat.X, toSeat.Z);

		return reader.LengthSquared() < 1e-6f
			? PokerTableLayout.YawTowardCentre(fallbackFacing)
			: PokerTableLayout.YawTowardCentre(reader.Normalized());
	}

	private Node3D SeatNodeFor(string playerId)
	{
		var index = _game.TurnOrder.IndexOf(playerId);
		if (index < 0 || index >= Seats.GetChildCount())
			return null;

		return Seats.GetChild(index) as Node3D;
	}

	/// <summary>Frees what belongs to somebody who is no longer at the table.</summary>
	private void DropStale(HashSet<string> seen)
	{
		Drop(_holeCards, seen, hand =>
		{
			foreach (var card in hand.Cards)
				card?.QueueFree();
		});

		Drop(_stacks, seen, pile => pile.QueueFree());
		Drop(_names, seen, label => label.QueueFree());
	}

	private static void Drop<T>(Dictionary<string, T> from, HashSet<string> seen, System.Action<T> free)
	{
		var stale = new List<string>();
		foreach (var entry in from)
		{
			if (!seen.Contains(entry.Key))
				stale.Add(entry.Key);
		}

		foreach (var playerId in stale)
		{
			free(from[playerId]);
			from.Remove(playerId);
		}
	}

	private static string NameOf(string playerId)
	{
		var player = PlayerRegistry.Instance?.GetPlayerById(playerId);

		return player != null && !string.IsNullOrWhiteSpace(player.Nickname)
			? player.Nickname
			: $"Jogador {playerId}";
	}
}
