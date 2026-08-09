using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

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
	[Export] public PackedScene CardScene;
	[Export] public PokerBoardPresenter BoardPresenter;
	[Export] public Node3D Seats;

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
	[Export] public float ChipFlightSeconds = 0.68f;

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
	[Export] public float ChipLandingSeconds = 0.28f;
	[Export] public float ChipCollectSeconds = 0.62f;
	[Export] public float ChipOrganizeSeconds = 0.46f;
	[Export] public float ChipCollectStagger = 0.08f;
	[Export] public float ChipPayoutSeconds = 0.82f;
	[Export] public float ChipPayoutStagger = 0.045f;
	[Export] public float PotColumnSpacing = 0.050f;
	[Export] public int ChipBatchPoolSize = 80;
	[Export] public int PrewarmedChipsPerBatch = 1;

	[ExportGroup("Showdown comparison")]
	/// <summary>Time left for everyone to read the exposed hole cards before ranking rearranges them.</summary>
	[Export] public float ShowdownRevealHoldSeconds = 2.5f;
	[Export] public float ShowdownCardSeconds = 0.72f;
	[Export] public float ShowdownRowStagger = 0.12f;
	[Export] public float ShowdownCardStagger = 0.035f;
	[Export] public float ShowdownRowSpacing = 0.118f;
	[Export] public float ShowdownCardSpacing = 0.075f;
	[Export] public float ShowdownArc = 0.034f;
	[Export] public Color ShowdownWinnerColor = new(0.35f, 1.0f, 0.48f);
	[Export] public Color ShowdownOtherColor = new(0.88f, 0.91f, 0.96f);

	/// <summary>Placeholder knuckle on wood. Any short, dry hit reads correctly.</summary>
	[Export] public AudioStream KnockSound;

	private enum ChipBatchPhase
	{
		Unused,
		ToBet,
		Landing,
		AtBet,
		ToPot,
		AtPotLoose,
		Organizing,
		InPot,
		ToWinner,
		AtWinner,
	}

	/// <summary>
	/// One persistent group of physical chips. The same nodes travel from the stack to the bet and
	/// later into the pot; changing phase never clears or rebuilds them.
	/// </summary>
	private sealed class ChipBatch
	{
		public PokerChipPile Pile;
		public ChipBatchPhase Phase;
		public string PlayerId = "";
		public int Amount;
		public float Progress;
		public float Delay;
		public Vector3 From;
		public Vector3 To;
		public Vector3 OrganizeFrom;
		public Vector3 OrganizeTo;
		public float StartSpread;
		public Basis Basis = Basis.Identity;
		public int Sequence;
		public bool JustStarted;
		public string WinnerId = "";
		public Basis FromBasis = Basis.Identity;
		public Basis ToBasis = Basis.Identity;
	}

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

	private sealed class ShowdownCardMove
	{
		public PokerCard Card;
		public Transform3D From;
		public Transform3D To;
		public float Delay;
		public bool Settled;
	}

	private readonly Dictionary<string, SeatHand> _holeCards = new();
	private bool _lastPickedUp;
	private int _lastGestureToken = -1;
	private AudioStreamPlayer3D _knock;

	/// <summary>
	/// Whether this peer's own pair has finished arriving. The hand view waits on it before playing
	/// the pick-up, so the player never reaches for a card that is still in the air.
	/// </summary>
	public bool LocalHandLanded { get; private set; }
	private readonly Dictionary<string, PokerChipPile> _stacks = new();
	private readonly Dictionary<string, Label3D> _names = new();
	private readonly List<ChipBatch> _chipBatches = new();
	private readonly Queue<PendingChipAction> _pendingChipActions = new();
	private readonly Dictionary<string, int> _observedStacks = new();
	private readonly Dictionary<string, int> _observedCommitted = new();
	private readonly Dictionary<string, int> _displayStacks = new();
	private readonly Dictionary<string, List<ChipRun>> _bankRuns = new();
	private readonly List<PokerCard> _showdownCardPool = new();
	private readonly List<Label3D> _showdownLabels = new();
	private readonly List<Color> _showdownLabelColors = new();
	private readonly List<ShowdownCardMove> _showdownMoves = new();
	private readonly List<string> _showdownDisplayOrder = new();
	private int _presentationHand = -1;
	private int _presentationActionSeq = -1;
	private PokerStreet _visibleStreet = PokerStreet.Preflop;
	private PokerStreet _requestedStreet = PokerStreet.Preflop;
	private bool _collecting;
	private bool _organizing;
	private bool _collectionRequested;
	private bool _settlementCollected;
	private bool _payoutStarted;
	private bool _payoutCompleted;
	private int _showdownHand = -1;
	private bool _showdownPresentationActive;
	private bool _showdownPresentationSettled;
	private float _showdownElapsed;
	private float _showdownRevealHoldElapsed;
	private bool _lastPresentationReady;
	private int _nextChipSequence;
	private MeshInstance3D _dealerButton;

	public override void _Ready()
	{
		_game = GetParent<PokerGame>();
		if (_game == null)
			return;

		SignalUtil.ConnectGuarded(_game, PokerGame.SignalName.HudStateUpdated,
			new Callable(this, MethodName.Refresh));

		BuildChipBatchPool();
		BuildShowdownPool();
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

		if (_showdownPresentationActive && shown)
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
		moved |= AdvanceShowdownPresentation((float)delta);

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
			ResetChipPresentation(spec);
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

	private void ResetChipPresentation(PokerLayoutSpec spec)
	{
		ResetShowdownPresentation();
		_presentationHand = _game.HandNumber;
		_presentationActionSeq = _game.ActionSeq;
		_visibleStreet = PokerStreet.Preflop;
		_requestedStreet = _game.Street;
		_collecting = false;
		_organizing = false;
		_collectionRequested = false;
		_settlementCollected = false;
		_payoutStarted = false;
		_payoutCompleted = false;
		_pendingChipActions.Clear();
		_observedStacks.Clear();
		_observedCommitted.Clear();
		_displayStacks.Clear();
		_bankRuns.Clear();
		_nextChipSequence = 0;

		foreach (var batch in _chipBatches)
			ResetBatch(batch);

		BoardPresenter.EnablePresentationGate(PokerStreet.Preflop);

		foreach (var playerId in _game.SeatOrder)
		{
			_displayStacks[playerId] = _game.StackOf(playerId);
			_bankRuns[playerId] = PokerChipStack.CreatePlayableBank(_game.StackOf(playerId));
			var blind = _game.BetOf(playerId);
			if (blind > 0)
				PlaceInitialBet(playerId, blind, spec);
		}

		RememberPublicChipState();
		if (_requestedStreet > _visibleStreet)
			_collectionRequested = true;
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
			ChipBatchPhase.Organizing, ChipBatchPhase.ToWinner)
		&& _visibleStreet >= _requestedStreet
		&& (BoardPresenter?.Settled ?? true);

	/// <summary>
	/// Instance ids of the physical chip visuals currently representing bets or the pot. Primarily a
	/// regression aid: their identity must survive landing, collection and organization.
	/// </summary>
	public IReadOnlyList<ulong> ActiveChipVisualIds()
	{
		var ids = new List<ulong>();
		foreach (var batch in _chipBatches)
		{
			if (batch.Phase == ChipBatchPhase.Unused || batch.Pile == null)
				continue;

			foreach (var child in batch.Pile.GetChildren())
			{
				if (child is Node3D { Visible: true } visual)
					ids.Add(visual.GetInstanceId());
			}
		}

		return ids;
	}

	/// <summary>Current table-local positions keyed by persistent chip instance.</summary>
	public IReadOnlyDictionary<ulong, Vector3> ActiveChipVisualPositions()
	{
		var positions = new Dictionary<ulong, Vector3>();
		foreach (var batch in _chipBatches)
		{
			if (batch.Phase == ChipBatchPhase.Unused || batch.Pile == null)
				continue;

			foreach (var child in batch.Pile.GetChildren())
			{
				if (child is Node3D { Visible: true } visual)
					positions[visual.GetInstanceId()] = batch.Pile.Transform * visual.Position;
			}
		}

		return positions;
	}

	/// <summary>Regression guard for the first rendered frame of a call or raise.</summary>
	public bool NewlyStartedBatchesAreAtTheirOrigin => _chipBatches.All(batch =>
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

	public bool BetsAreVisuallyLoose => _chipBatches.Any(batch =>
		batch.Phase == ChipBatchPhase.AtBet && batch.Pile.Spread > 0.95f);

	public bool PotIsLooseWhileOrganizing => _chipBatches.Any(batch =>
		batch.Phase == ChipBatchPhase.Organizing && batch.Progress < 0.25f
		&& batch.Pile.Spread > 0.70f);

	public bool PotIsOrganizedTower
	{
		get
		{
			var found = false;
			foreach (var batch in _chipBatches)
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
	public int PotDenominationColumnCount => _chipBatches
		.Where(batch => batch.Phase == ChipBatchPhase.InPot)
		.Select(DenominationOf).Where(value => value > 0).Distinct().Count();

	public bool PayoutStarted => _payoutStarted;
	public bool PayoutCompleted => _payoutCompleted;
	public int ChipsDeliveredToWinners => _chipBatches.Count(batch =>
		batch.Phase == ChipBatchPhase.AtWinner);
	public int PayoutRecipientCount => _chipBatches
		.Where(batch => batch.Phase == ChipBatchPhase.AtWinner)
		.Select(batch => batch.WinnerId).Where(id => !string.IsNullOrEmpty(id)).Distinct().Count();

	/// <summary>
	/// Stack value currently represented on the cloth. This deliberately trails the authoritative
	/// value until the corresponding physical batch starts moving, keeping removal and take-off in
	/// the same rendered frame.
	/// </summary>
	public int DisplayedStackOf(string playerId) =>
		_displayStacks.GetValueOrDefault(playerId, _game?.StackOf(playerId) ?? 0);

	private void BuildChipBatchPool()
	{
		for (var i = _chipBatches.Count; i < Mathf.Max(1, ChipBatchPoolSize); i++)
		{
			var pile = new PokerChipPile
			{
				Name = $"ChipBatch{i}",
				ChipScene = _game?.ChipScene,
				Scatter = ChipScatter,
				CombineRunsIntoColumns = true,
				LooseWhenSpread = true,
				Spread = 0.0f,
				// The batch root owns take-off and landing. Letting PokerChipPile start its own
				// settle-drop here made the chip jump vertically for a frame before the throw.
				SettleSeconds = 0.0f,
			};

			AddChild(pile);
			pile.Prewarm(Mathf.Max(1, PrewarmedChipsPerBatch));
			pile.Visible = false;
			_chipBatches.Add(new ChipBatch { Pile = pile, Phase = ChipBatchPhase.Unused });
		}
	}

	private ChipBatch AcquireBatch()
	{
		foreach (var batch in _chipBatches)
		{
			if (batch.Phase == ChipBatchPhase.Unused)
				return batch;
		}

		// A very long hand may exceed the warm pool. Growing here is a safe fallback; normal play
		// never takes it, so actions remain allocation-free.
		var pile = new PokerChipPile
		{
			Name = $"ChipBatch{_chipBatches.Count}",
			ChipScene = _game?.ChipScene,
			Scatter = ChipScatter,
			CombineRunsIntoColumns = true,
			LooseWhenSpread = true,
			SettleSeconds = 0.0f,
		};
		AddChild(pile);
		pile.Prewarm(Mathf.Max(1, PrewarmedChipsPerBatch));
		var extra = new ChipBatch { Pile = pile, Phase = ChipBatchPhase.Unused };
		_chipBatches.Add(extra);
		return extra;
	}

	private static void ResetBatch(ChipBatch batch)
	{
		// Hide first: a pooled batch may still be parked at last action's destination.
		batch.Pile.Visible = false;
		batch.Phase = ChipBatchPhase.Unused;
		batch.PlayerId = "";
		batch.Amount = 0;
		batch.Progress = 0.0f;
		batch.Delay = 0.0f;
		batch.Sequence = -1;
		batch.JustStarted = false;
		batch.WinnerId = "";
		batch.FromBasis = Basis.Identity;
		batch.ToBasis = Basis.Identity;
		batch.Pile.FlightProgress = 1.0f;
		batch.Pile.Spread = 0.0f;
		batch.Pile.LooseSlotOffset = 0;
		batch.Pile.Clear();
		batch.Pile.Transform = Transform3D.Identity;
	}

	private void PlaceInitialBet(string playerId, int amount, PokerLayoutSpec spec)
	{
		if (!TrySeatChipPlaces(playerId, spec, out var basis, out _, out var bet))
			return;

		foreach (var run in PokerChipStack.Decompose(amount))
		{
			for (var chip = 0; chip < run.Count; chip++)
			{
				var batch = AcquireBatch();
				batch.PlayerId = playerId;
				batch.Amount = run.Denomination;
				batch.Basis = basis;
				batch.Sequence = _nextChipSequence++;
				batch.Phase = ChipBatchPhase.AtBet;
				batch.To = bet;
				batch.Pile.Visible = false;
				batch.Pile.Transform = new Transform3D(basis, batch.To);
				batch.Pile.LooseSlotOffset = NextBetLooseSlot(playerId);
				batch.Pile.Spread = 1.0f;
				batch.Pile.FlightProgress = 1.0f;
				batch.Pile.SetRuns(new[] { new ChipRun(run.Denomination, 1) });
				batch.Pile.Visible = true;
			}
		}
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

		var physicalIndex = 0;
		foreach (var run in payment)
		{
			for (var chip = 0; chip < run.Count; chip++)
			{
				var batch = AcquireBatch();
				batch.PlayerId = action.PlayerId;
				batch.Amount = run.Denomination;
				batch.Basis = basis;
				batch.Sequence = _nextChipSequence++;
				batch.To = bet;
				batch.Progress = 0.0f;
				batch.Delay = physicalIndex++ * 0.025f;
				batch.Phase = ChipBatchPhase.ToBet;
				var departure = _bankRuns.TryGetValue(action.PlayerId, out var bankAfter)
					? PaymentDepartureOffset(bankAfter, run.Denomination, chip, bankPile)
					: Vector3.Up * (bankPile?.TopHeight ?? 0.0f);
				batch.From = stack + basis * departure;

				// Configure while hidden and reveal at the physical source, never at the pooled
				// node's previous destination. One chip per batch also prevents a group from briefly
				// changing shape when its denominations separate.
				batch.Pile.Visible = false;
				batch.Pile.Transform = new Transform3D(basis, batch.From);
				batch.Pile.LooseSlotOffset = NextBetLooseSlot(action.PlayerId);
				batch.Pile.Spread = 0.0f;
				batch.Pile.FlightProgress = 0.0f;
				batch.Pile.SetRuns(new[] { new ChipRun(run.Denomination, 1) });
				batch.JustStarted = true;
				batch.Pile.Visible = true;
			}
		}
		return physicalIndex > 0;
	}

	private int NextBetLooseSlot(string playerId)
	{
		var next = 0;
		foreach (var batch in _chipBatches)
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

		foreach (var batch in _chipBatches)
		{
			switch (batch.Phase)
			{
				case ChipBatchPhase.ToBet:
					AdvanceToBet(batch, delta);
					moved = true;
					break;

				case ChipBatchPhase.Landing:
					AdvanceLanding(batch, delta);
					moved = true;
					break;

				case ChipBatchPhase.ToPot:
					AdvanceToPot(batch, delta);
					moved = true;
					break;

				case ChipBatchPhase.Organizing:
					AdvanceOrganization(batch, delta);
					moved = true;
					break;

				case ChipBatchPhase.ToWinner:
					AdvanceToWinner(batch, delta);
					moved = true;
					break;
			}
		}

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
			BeginPayout();
			moved = true;
		}

		if (_payoutStarted && !_payoutCompleted && !HasPhase(ChipBatchPhase.ToWinner))
		{
			_payoutCompleted = true;
			foreach (var winner in _game.Winners.Keys)
				_displayStacks[winner] = _game.StackOf(winner);
			moved = true;
		}

		return moved;
	}

	private void AdvanceToBet(ChipBatch batch, float delta)
	{
		// Preserve one complete rendered frame at the real source. Besides making the take-off readable,
		// this prevents a newly reused batch from ever exposing its old destination transform.
		if (batch.JustStarted)
		{
			batch.JustStarted = false;
			batch.Pile.Transform = new Transform3D(batch.Basis, batch.From);
			return;
		}
		if (batch.Delay > 0.0f)
		{
			batch.Delay = Mathf.Max(0.0f, batch.Delay - delta);
			return;
		}

		batch.Progress = Mathf.Min(1.0f,
			batch.Progress + delta / Mathf.Max(ChipFlightSeconds, 0.01f));
		var path = PokerMotion.ChipThrow(
			new Vector2(batch.From.X, batch.From.Z),
			new Vector2(batch.To.X, batch.To.Z),
			batch.Progress, ChipFlightArc,
			PokerChipPile.Noise(batch.Amount, 14) * 0.010f);
		var wave = Mathf.Sin(batch.Progress * Mathf.Pi);
		var yaw = PokerChipPile.Noise(batch.Amount, 13) * 0.10f * wave;

		batch.Pile.Transform = new Transform3D(batch.Basis * new Basis(Vector3.Up, yaw), path);
		batch.Pile.FlightProgress = batch.Progress;
		batch.Pile.Spread = PokerMotion.Smooth(batch.Progress);

		if (batch.Progress < 1.0f)
			return;

		batch.Progress = 0.0f;
		batch.Phase = ChipBatchPhase.Landing;
		batch.Pile.FlightProgress = 1.0f;
	}

	private void AdvanceLanding(ChipBatch batch, float delta)
	{
		batch.Progress = Mathf.Min(1.0f,
			batch.Progress + delta / Mathf.Max(ChipLandingSeconds, 0.01f));
		var decay = 1.0f - batch.Progress;
		var bounce = Mathf.Abs(Mathf.Sin(batch.Progress * Mathf.Pi * 2.0f)) * decay * 0.009f;
		batch.Pile.Transform = new Transform3D(batch.Basis, batch.To + Vector3.Up * bounce);

		if (batch.Progress >= 1.0f)
			batch.Phase = ChipBatchPhase.AtBet;
	}

	private void BeginCollection()
	{
		_collecting = true;
		_organizing = false;
		var order = 0;
		var looseSlot = 0;
		var pot = BoardPresenter.PotPosition;
		foreach (var batch in _chipBatches)
		{
			if (batch.Phase == ChipBatchPhase.InPot)
				looseSlot += batch.Pile.ChipCount;
		}

		foreach (var batch in _chipBatches)
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

	private void AdvanceToPot(ChipBatch batch, float delta)
	{
		if (batch.Delay > 0.0f)
		{
			batch.Delay -= delta;
			return;
		}

		batch.Progress = Mathf.Min(1.0f,
			batch.Progress + delta / Mathf.Max(ChipCollectSeconds, 0.01f));
		var path = PokerMotion.ChipThrow(
			new Vector2(batch.From.X, batch.From.Z),
			new Vector2(batch.To.X, batch.To.Z),
			batch.Progress, 0.028f,
			PokerChipPile.Noise(batch.Amount, 16) * 0.012f);
		// All batches finish on the same world-aligned loose lattice. Interpolating the yaw during
		// collection avoids rotating a player's already-landed chips in a single frame.
		var rotation = new Transform3D(batch.Basis, Vector3.Zero).InterpolateWith(
			new Transform3D(Basis.Identity, Vector3.Zero), PokerMotion.Smooth(batch.Progress)).Basis;
		batch.Pile.Transform = new Transform3D(rotation, path);
		batch.Pile.FlightProgress = batch.Progress;
		batch.Pile.LooseLayoutProgress = PokerMotion.Smooth(batch.Progress);

		if (batch.Progress >= 1.0f)
		{
			batch.Phase = ChipBatchPhase.AtPotLoose;
			batch.Pile.FlightProgress = 1.0f;
		}
	}

	private void BeginOrganization()
	{
		var potBatches = new List<ChipBatch>();
		foreach (var batch in _chipBatches)
		{
			if (batch.Phase is ChipBatchPhase.AtPotLoose or ChipBatchPhase.InPot)
				potBatches.Add(batch);
		}

		if (potBatches.Count == 0)
		{
			_organizing = true;
			return;
		}

		_organizing = true;
		var centre = BoardPresenter.PotPosition;
		var reader = new Basis(Vector3.Up, ReaderYaw(Vector2.Down));
		potBatches.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
		var denominations = potBatches.Select(DenominationOf).Where(value => value > 0)
			.Distinct().OrderByDescending(value => value).ToList();
		var heights = denominations.ToDictionary(value => value, _ => 0.0f);

		for (var i = 0; i < potBatches.Count; i++)
		{
			var batch = potBatches[i];
			var denomination = DenominationOf(batch);
			var lane = Mathf.Max(0, denominations.IndexOf(denomination));
			var x = (lane - (denominations.Count - 1) * 0.5f) * PotColumnSpacing;
			batch.OrganizeFrom = batch.Pile.Position;
			batch.OrganizeTo = centre + reader * new Vector3(x, heights.GetValueOrDefault(denomination), 0.0f);
			batch.FromBasis = batch.Pile.Basis;
			batch.ToBasis = reader;
			batch.StartSpread = batch.Pile.Spread;
			batch.Progress = 0.0f;
			batch.Phase = ChipBatchPhase.Organizing;
			heights[denomination] = heights.GetValueOrDefault(denomination) + batch.Pile.TopHeight;
		}
	}

	private void AdvanceOrganization(ChipBatch batch, float delta)
	{
		batch.Progress = Mathf.Min(1.0f,
			batch.Progress + delta / Mathf.Max(ChipOrganizeSeconds, 0.01f));
		var t = PokerMotion.Smooth(batch.Progress);
		var basis = new Transform3D(batch.FromBasis, Vector3.Zero).InterpolateWith(
			new Transform3D(batch.ToBasis, Vector3.Zero), t).Basis;
		batch.Pile.Transform = new Transform3D(
			basis, batch.OrganizeFrom.Lerp(batch.OrganizeTo, t));
		batch.Pile.Spread = Mathf.Lerp(batch.StartSpread, 0.0f, t);

		if (batch.Progress >= 1.0f)
			batch.Phase = ChipBatchPhase.InPot;
	}

	private static int DenominationOf(ChipBatch batch) =>
		batch?.Pile?.Runs.Count > 0 ? batch.Pile.Runs[0].Denomination : 0;

	private bool ShouldBeginPayout()
	{
		if (_payoutStarted || _payoutCompleted || !_game.HandSettled || !_settlementCollected
			|| _collecting || _organizing || _collectionRequested
			|| HasPhase(ChipBatchPhase.ToBet, ChipBatchPhase.Landing,
				ChipBatchPhase.ToPot, ChipBatchPhase.Organizing))
			return false;

		// In an uncontested hand there is no comparison to wait for. At a showdown, however, the
		// chips only leave after everyone has read the revealed hands and the ranking is in place.
		if (_game.RevealedHoleCards.Count > 0 && !_showdownPresentationSettled)
			return false;

		return _game.Winners.Count > 0
			&& _chipBatches.Any(batch => batch.Phase == ChipBatchPhase.InPot);
	}

	private void BeginPayout()
	{
		var winners = _game.Winners.Where(entry => entry.Value > 0)
			.OrderBy(entry => System.Array.IndexOf(_game.SeatOrder, entry.Key)).ToList();
		var potChips = _chipBatches.Where(batch => batch.Phase == ChipBatchPhase.InPot)
			.OrderByDescending(batch => batch.Amount).ThenBy(batch => batch.Sequence).ToList();
		if (winners.Count == 0 || potChips.Count == 0)
			return;

		_payoutStarted = true;
		var assignments = new Dictionary<ChipBatch, string>();
		var recipients = AssignPayoutRecipients(
			potChips.Select(batch => batch.Amount).ToList(), _game.Winners, _game.SeatOrder);
		for (var i = 0; i < potChips.Count && i < recipients.Length; i++)
			assignments[potChips[i]] = recipients[i];

		var arrivals = new Dictionary<(string PlayerId, int Denomination), int>();
		var order = 0;
		foreach (var batch in assignments.Keys.OrderBy(batch => batch.Sequence))
		{
			var winnerId = assignments[batch];
			if (!TrySeatChipPlaces(winnerId, BoardPresenter.Spec,
					out var basis, out var stack, out _))
				continue;

			var denomination = DenominationOf(batch);
			var key = (winnerId, denomination);
			var arrival = arrivals.GetValueOrDefault(key);
			arrivals[key] = arrival + 1;

			batch.WinnerId = winnerId;
			batch.From = batch.Pile.Position;
			batch.To = stack + basis * WinnerStackOffset(
				winnerId, denomination, arrival, batch.Pile);
			batch.FromBasis = batch.Pile.Basis;
			batch.ToBasis = basis;
			batch.Progress = 0.0f;
			batch.Delay = order++ * ChipPayoutStagger;
			batch.JustStarted = true;
			batch.Phase = ChipBatchPhase.ToWinner;
			batch.Pile.FlightProgress = 0.0f;
			batch.Pile.Spread = 0.0f;
		}
	}

	/// <summary>
	/// Maps physical pot chips to every paid player. Exact subsets are preferred; if a split requires
	/// change that is not physically on the cloth (38/37 from three 25s, for example), it still gives
	/// both players a visible delivery and lets the authoritative stack settle the change afterward.
	/// </summary>
	public static string[] AssignPayoutRecipients(
		IReadOnlyList<int> chipValues, IReadOnlyDictionary<string, int> awards,
		IReadOnlyList<string> seatOrder)
	{
		if (chipValues == null || awards == null)
			return System.Array.Empty<string>();

		var winners = awards.Where(entry => entry.Value > 0)
			.OrderBy(entry => SeatIndexOf(seatOrder, entry.Key)).ToList();
		if (winners.Count == 0)
			return new string[chipValues.Count];

		var remaining = winners.ToDictionary(entry => entry.Key, entry => entry.Value);
		var recipients = Enumerable.Repeat("", chipValues.Count).ToArray();
		var available = Enumerable.Range(0, chipValues.Count).ToList();

		foreach (var winner in winners.OrderBy(entry => entry.Value))
		{
			if (available.Count == 0)
				break;
			var chosen = available.Where(index => chipValues[index] <= winner.Value)
				.OrderByDescending(index => chipValues[index]).FirstOrDefault(-1);
			if (chosen < 0)
				chosen = available[^1];
			recipients[chosen] = winner.Key;
			remaining[winner.Key] -= chipValues[chosen];
			available.Remove(chosen);
		}

		foreach (var index in available)
		{
			var value = chipValues[index];
			var fitting = winners.Where(entry => remaining[entry.Key] >= value)
				.OrderByDescending(entry => remaining[entry.Key]).FirstOrDefault();
			var winnerId = !string.IsNullOrEmpty(fitting.Key)
				? fitting.Key
				: winners.OrderByDescending(entry => remaining[entry.Key]).First().Key;
			recipients[index] = winnerId;
			remaining[winnerId] -= value;
		}

		return recipients;
	}

	private static int SeatIndexOf(IReadOnlyList<string> seats, string playerId)
	{
		if (seats == null)
			return int.MaxValue;
		for (var i = 0; i < seats.Count; i++)
		{
			if (seats[i] == playerId)
				return i;
		}
		return int.MaxValue;
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

	private void AdvanceToWinner(ChipBatch batch, float delta)
	{
		if (batch.JustStarted)
		{
			batch.JustStarted = false;
			batch.Pile.Transform = new Transform3D(batch.FromBasis, batch.From);
			return;
		}
		if (batch.Delay > 0.0f)
		{
			batch.Delay = Mathf.Max(0.0f, batch.Delay - delta);
			return;
		}

		batch.Progress = Mathf.Min(1.0f,
			batch.Progress + delta / Mathf.Max(ChipPayoutSeconds, 0.01f));
		var t = PokerMotion.Smooth(batch.Progress);
		var position = PokerMotion.ChipThrow(batch.From, batch.To, batch.Progress,
			0.055f, PokerChipPile.Noise(batch.Sequence, 31) * 0.014f);
		var basis = new Transform3D(batch.FromBasis, Vector3.Zero).InterpolateWith(
			new Transform3D(batch.ToBasis, Vector3.Zero), t).Basis;
		batch.Pile.Transform = new Transform3D(basis, position);
		batch.Pile.FlightProgress = batch.Progress;

		if (batch.Progress < 1.0f)
			return;

		batch.Pile.Transform = new Transform3D(batch.ToBasis, batch.To);
		batch.Pile.FlightProgress = 1.0f;
		batch.Phase = ChipBatchPhase.AtWinner;
		_displayStacks[batch.WinnerId] = Mathf.Min(_game.StackOf(batch.WinnerId),
			_displayStacks.GetValueOrDefault(batch.WinnerId) + batch.Amount);
	}

	private bool HasPhase(params ChipBatchPhase[] phases)
	{
		foreach (var batch in _chipBatches)
		{
			foreach (var phase in phases)
			{
				if (batch.Phase == phase)
					return true;
			}
		}

		return false;
	}

	/// <summary>Whether the ranked five-card rows have finished reaching their comparison layout.</summary>
	public bool ShowdownPresentationSettled => _showdownPresentationSettled;

	/// <summary>Elapsed portion of the face-up reading beat before ranking begins.</summary>
	public float ShowdownRevealHoldElapsed => _showdownRevealHoldElapsed;

	/// <summary>Players in the same best-to-worst order currently shown on the cloth.</summary>
	public IReadOnlyList<string> ShowdownDisplayOrder => _showdownDisplayOrder;

	public int ShowdownDisplayedCardCount => _showdownMoves.Count;

	public string DebugShowdownState()
	{
		var returning = 0;
		var phases = new Dictionary<ChipBatchPhase, int>();
		foreach (var hand in _holeCards.Values)
		{
			if (hand.Returning)
				returning++;
		}
		foreach (var batch in _chipBatches)
			phases[batch.Phase] = phases.GetValueOrDefault(batch.Phase) + 1;
		var moving = new List<string>();
		foreach (var batch in _chipBatches)
		{
			if (batch.Phase is ChipBatchPhase.ToBet or ChipBatchPhase.Landing
				or ChipBatchPhase.ToPot or ChipBatchPhase.Organizing)
				moving.Add($"{batch.Phase}:{batch.Progress:F2}/{batch.Delay:F2}");
		}

		return $"active={_showdownPresentationActive} settled={_showdownPresentationSettled} "
			+ $"hand={_game?.HandNumber}/{_showdownHand} result={_game?.HandSettled} "
			+ $"reveals={_game?.RevealedHoleCards.Count} board={BoardPresenter?.Settled} "
			+ $"pending={_pendingChipActions.Count} collect={_collecting}/{_organizing}/{_collectionRequested} "
			+ $"returning={returning} visible={_visibleStreet} requested={_requestedStreet} "
			+ $"phases={string.Join(",", phases)} moving={string.Join(",", moving)}";
	}

	private void BuildShowdownPool()
	{
		const int MaximumRows = 4;
		const int CardsPerRow = 5;

		for (var i = 0; i < MaximumRows * CardsPerRow; i++)
		{
			var card = _game?.CreateCard(CardScene);
			if (card == null)
				break;

			card.Name = $"ShowdownCard{i}";
			card.Visible = false;
			AddChild(card);
			_showdownCardPool.Add(card);
		}

		for (var i = 0; i < MaximumRows; i++)
		{
			var label = new Label3D
			{
				Name = $"ShowdownLabel{i}",
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				NoDepthTest = true,
				PixelSize = 0.00020f,
				FontSize = 64,
				OutlineSize = 10,
				Visible = false,
			};

			AddChild(label);
			_showdownLabels.Add(label);
			_showdownLabelColors.Add(ShowdownOtherColor);
		}
	}

	private void ResetShowdownPresentation()
	{
		_showdownPresentationActive = false;
		_showdownPresentationSettled = false;
		_showdownHand = -1;
		_showdownElapsed = 0.0f;
		_showdownRevealHoldElapsed = 0.0f;
		_showdownMoves.Clear();
		_showdownDisplayOrder.Clear();

		foreach (var card in _showdownCardPool)
			card.Visible = false;
		foreach (var label in _showdownLabels)
			label.Visible = false;
	}

	private bool AdvanceShowdownPresentation(float delta)
	{
		if (!_showdownPresentationActive)
			return TryStartShowdownPresentation(delta);

		if (_showdownPresentationSettled)
			return false;

		_showdownElapsed += delta;
		var allSettled = true;
		for (var index = 0; index < _showdownMoves.Count; index++)
		{
			var move = _showdownMoves[index];
			var raw = (_showdownElapsed - move.Delay) / Mathf.Max(ShowdownCardSeconds, 0.01f);
			var t = Mathf.Clamp(raw, 0.0f, 1.0f);
			if (t < 1.0f)
				allSettled = false;

			var position = PokerMotion.CardThrow(
				move.From.Origin, move.To.Origin, t, ShowdownArc,
				PokerChipPile.Noise(index, 24) * 0.010f);
			var transform = move.From.InterpolateWith(move.To, PokerMotion.Smooth(t));
			move.Card.Transform = new Transform3D(transform.Basis, position);
			move.Settled = t >= 1.0f;
		}

		for (var row = 0; row < _showdownDisplayOrder.Count && row < _showdownLabels.Count; row++)
		{
			var delay = row * ShowdownRowStagger + ShowdownCardSeconds * 0.72f;
			var alpha = PokerMotion.Smooth(Mathf.Clamp((_showdownElapsed - delay) / 0.24f, 0.0f, 1.0f));
			var color = _showdownLabelColors[row];
			color.A *= alpha;
			_showdownLabels[row].Modulate = color;
		}

		_showdownPresentationSettled = allSettled;
		return true;
	}

	private bool TryStartShowdownPresentation(float delta)
	{
		if (_game.HandNumber <= 0 || _showdownHand == _game.HandNumber
			|| !_game.HandSettled || _game.RevealedHoleCards.Count == 0
			|| BoardPresenter == null || !BoardPresenter.Settled
			|| _collecting || _organizing || _collectionRequested
			|| HasPhase(ChipBatchPhase.ToBet, ChipBatchPhase.Landing,
				ChipBatchPhase.ToPot, ChipBatchPhase.Organizing))
		{
			_showdownRevealHoldElapsed = 0.0f;
			return false;
		}

		foreach (var hand in _holeCards.Values)
		{
			if (hand.Returning)
			{
				_showdownRevealHoldElapsed = 0.0f;
				return false;
			}
		}

		// The cards are now face up on the felt. Leave the original hands intact for a deliberate
		// reading beat before the best-five comparison borrows and rearranges those visuals.
		_showdownRevealHoldElapsed += Mathf.Max(0.0f, delta);
		if (_showdownRevealHoldElapsed < Mathf.Max(0.0f, ShowdownRevealHoldSeconds))
			return false;

		var ranked = new List<(string PlayerId, PokerHandRank Rank, int[] Cards)>();
		foreach (var entry in _game.RevealedHoleCards)
		{
			var rank = PokerHandEvaluator.Evaluate(entry.Value, _game.Board);
			var cards = PokerHandEvaluator.BestFive(entry.Value, _game.Board);
			if (cards.Length == 5)
				ranked.Add((entry.Key, rank, cards));
		}

		ranked.Sort((left, right) =>
		{
			var strength = right.Rank.CompareTo(left.Rank);
			if (strength != 0)
				return strength;

			return System.Array.IndexOf(_game.SeatOrder, left.PlayerId)
				.CompareTo(System.Array.IndexOf(_game.SeatOrder, right.PlayerId));
		});

		if (ranked.Count == 0 || ranked.Count * 5 > _showdownCardPool.Count)
			return false;

		_showdownHand = _game.HandNumber;
		_showdownPresentationActive = true;
		_showdownPresentationSettled = false;
		_showdownElapsed = 0.0f;
		_showdownMoves.Clear();
		_showdownDisplayOrder.Clear();

		var spec = BoardPresenter.Spec;
		var reader = new Basis(Vector3.Up, ReaderYaw(Vector2.Down));
		var rowCentre = (ranked.Count - 1) * 0.5f;
		var visualIndex = 0;

		for (var row = 0; row < ranked.Count; row++)
		{
			var result = ranked[row];
			_showdownDisplayOrder.Add(result.PlayerId);
			var rowZ = (row - rowCentre) * ShowdownRowSpacing;
			var isWinner = _game.Winners.ContainsKey(result.PlayerId);

			for (var cardIndex = 0; cardIndex < result.Cards.Length; cardIndex++)
			{
				var cardId = result.Cards[cardIndex];
				var card = _showdownCardPool[visualIndex];
				var source = ShowdownSourceTransform(result.PlayerId, cardId);
				var x = (cardIndex - 2.0f) * ShowdownCardSpacing;
				var targetPosition = reader * new Vector3(
					x, spec.CardThickness * 0.5f + 0.004f + row * 0.0005f, rowZ);
				var target = new Transform3D(reader * PokerCard.Orientation(false), targetPosition);

				card.Configure(cardId, spec);
				card.Transform = source;
				card.Visible = true;
				_showdownMoves.Add(new ShowdownCardMove
				{
					Card = card,
					From = source,
					To = target,
					Delay = row * ShowdownRowStagger + cardIndex * ShowdownCardStagger,
				});
				visualIndex++;
			}

			var label = _showdownLabels[row];
			var place = row + 1;
			label.Text = isWinner
				? $"VENCEDOR · {NameOf(result.PlayerId)} — {result.Rank.Describe()}"
				: $"{place}º · {NameOf(result.PlayerId)} — {result.Rank.Describe()}";
			label.Position = reader * new Vector3(-0.31f, 0.030f, rowZ);
			label.Visible = true;
			_showdownLabelColors[row] = isWinner ? ShowdownWinnerColor : ShowdownOtherColor;
			label.Modulate = _showdownLabelColors[row] with { A = 0.0f };
		}

		for (var index = visualIndex; index < _showdownCardPool.Count; index++)
			_showdownCardPool[index].Visible = false;
		for (var row = ranked.Count; row < _showdownLabels.Count; row++)
			_showdownLabels[row].Visible = false;

		// Swap at identical transforms: the comparison cards are already exactly over their physical
		// sources when those sources disappear, so the eye sees one card begin moving, not a spawn.
		for (var index = 0; index < PokerDeal.BoardCount; index++)
		{
			var source = BoardPresenter.BoardCardNodeAt(index);
			if (source != null)
				source.Visible = false;
		}

		foreach (var result in ranked)
		{
			if (!_holeCards.TryGetValue(result.PlayerId, out var hand))
				continue;
			foreach (var source in hand.Cards)
				source.Visible = false;
		}

		return true;
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
