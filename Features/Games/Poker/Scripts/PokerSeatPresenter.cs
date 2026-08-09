using System.Collections.Generic;
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
	[Export] public float DealArc = 0.05f;

	/// <summary>
	/// How long an opponent's pair sits on the cloth before they take it up. The local player's
	/// stays until they pick it up themselves.
	/// </summary>
	[Export] public float OpponentPickUpDelay = 1.1f;

	[ExportGroup("Folding")]
	/// <summary>How long the thrown pair takes to reach the muck.</summary>
	[Export] public float MuckSeconds = 0.55f;

	/// <summary>How high it arcs on the way, so it is thrown rather than pushed.</summary>
	[Export] public float MuckArc = 0.09f;

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
	}

	[ExportGroup("Betting chips")]
	/// <summary>How long chips take to go from a stack to the middle.</summary>
	[Export] public float ChipFlightSeconds = 0.95f;

	/// <summary>How high they are carried before being let go.</summary>
	[Export] public float ChipFlightArc = 0.15f;

	/// <summary>How far a thrown chip ends up from the middle of its stack.</summary>
	[Export] public float ChipScatter = 0.011f;

	/// <summary>How tidily a player keeps their OWN stack. Not perfectly, but close.</summary>
	[Export] public float StackScatter = 0.0025f;

	/// <summary>Placeholder knuckle on wood. Any short, dry hit reads correctly.</summary>
	[Export] public AudioStream KnockSound;

	/// <summary>Chips on their way from a player's stack to their bet.</summary>
	private sealed class ChipFlight
	{
		public PokerChipPile Pile;

		/// <summary>1 means nothing is in the air.</summary>
		public float Progress = 1.0f;

		public int Amount;

		/// <summary>What the bet pile is drawing. Lags the real bet until the chips land.</summary>
		public int Shown;

		/// <summary>
		/// The chips currently in the air, and the ones already lying at the bet.
		///
		/// Kept as chips rather than as a number so a throw KEEPS what it threw: the run list handed
		/// to the flight is the same one appended to the bet when it lands, and nothing changes
		/// denomination or colour in mid-air.
		/// </summary>
		public readonly List<ChipRun> InAir = new();

		public readonly List<ChipRun> Resting = new();

		public Vector2 From;
		public Vector2 To;
	}

	/// <summary>
	/// How far into the throw the chips are still being carried. Past this they are let go, which is
	/// where both the fall and the spread start.
	/// </summary>
	private const float CarryUntil = 0.65f;

	private readonly Dictionary<string, SeatHand> _holeCards = new();
	private readonly Dictionary<string, ChipFlight> _flights = new();
	private bool _lastPickedUp;
	private int _lastGestureToken = -1;
	private AudioStreamPlayer3D _knock;

	/// <summary>
	/// Whether this peer's own pair has finished arriving. The hand view waits on it before playing
	/// the pick-up, so the player never reaches for a card that is still in the air.
	/// </summary>
	public bool LocalHandLanded { get; private set; }
	private readonly Dictionary<string, PokerChipPile> _bets = new();
	private readonly Dictionary<string, PokerChipPile> _stacks = new();
	private readonly Dictionary<string, Label3D> _names = new();
	private MeshInstance3D _dealerButton;

	public override void _Ready()
	{
		_game = GetParent<PokerGame>();
		if (_game == null)
			return;

		SignalUtil.ConnectGuarded(_game, PokerGame.SignalName.HudStateUpdated,
			new Callable(this, MethodName.Refresh));
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
			hand.Hand = _game.HandNumber;
			hand.OnTable = 0.0f;
			hand.Revealed = false;
			hand.Folded = false;
			hand.Mucked = 0.0f;

			var seat = System.Array.IndexOf(_game.SeatOrder, playerId);
			var seats = Mathf.Max(1, _game.SeatOrder.Length);

			for (var i = 0; i < hand.Cards.Length; i++)
			{
				hand.Dealt[i] = 0.0f;
				hand.Wait[i] = (i * seats + Mathf.Max(seat, 0)) * DealStagger;
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
		}

		if (revealed != null && !hand.Revealed)
		{
			// A showdown puts them back on the cloth, face up and already in place.
			hand.Revealed = true;
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
			else if (card.CardId != 0 || !card.IsFaceDown)
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
			if (CardScene?.Instantiate() is not PokerCard card)
				return null;

			AddChild(card);
			hand.Cards[i] = card;
		}

		_holeCards[playerId] = hand;
		return hand;
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

		if (_game.HandNumber > 0 && hand.Folded && !shown)
		{
			PlaceMuck(playerId, hand, facing, spec, yaw);
			return;
		}

		var taken = !shown && HasTakenCardsUp(playerId, hand);

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

			// Ease out on the way over, and arc so the card is thrown rather than slid.
			var arrival = 1.0f - Mathf.Pow(1.0f - hand.Dealt[i], 3.0f);
			var position = deck.Lerp(seated, arrival);
			position.Y += Mathf.Sin(arrival * Mathf.Pi) * DealArc;

			// Configure only RECORDS that a card is face down; turning it over is the caller's job.
			// Without this the pair lay face up showing a blank white placeholder.
			card.Transform = new Transform3D(turned * PokerCard.Orientation(!shown), position);
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
		var settled = SmoothStep(hand.Mucked);

		for (var i = 0; i < hand.Cards.Length; i++)
		{
			var card = hand.Cards[i];
			card.Visible = true;

			var slot = seat * hand.Cards.Length + i;

			var rest = muck + new Vector3(
				PokerChipPile.Noise(slot, 0) * MuckSpread,
				(slot + 0.5f) * spec.CardThickness * 1.6f,
				PokerChipPile.Noise(slot, 1) * MuckSpread);

			var from = MuckSource(hand, facing, spec, i);

			var position = from.Lerp(rest, settled);
			position.Y += Mathf.Sin(settled * Mathf.Pi) * MuckArc;

			// It turns as it goes. A hand thrown in flat and square reads as a hand being dealt
			// backwards; a discard lands askew.
			var spun = Mathf.LerpAngle(yaw, yaw + PokerChipPile.Noise(slot, 2) * Mathf.Pi, settled);

			card.Transform = new Transform3D(
				new Basis(Vector3.Up, spun) * PokerCard.Orientation(true), position);
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

		// Out of their hands. The local player's own hand hangs off their camera and this table has
		// no idea where that is, but everybody holds their cards in the same place relative to their
		// own chair — which is right for the player throwing them and for the three watching.
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

		foreach (var flight in _flights.Values)
		{
			if (flight.Progress >= 1.0f)
				continue;

			flight.Progress = Mathf.Min(1.0f, flight.Progress + (float)delta / Mathf.Max(ChipFlightSeconds, 0.01f));
			moved = true;

			// Chips let go of together do not stay in a column: they open up as they fall. The whole
			// spread happens during the drop, so what leaves the hand is a stack and what reaches
			// the cloth is a scatter.
			flight.Pile.Spread = Mathf.Clamp((flight.Progress - CarryUntil) / (1.0f - CarryUntil), 0.0f, 1.0f);

			// Landed: the bet pile takes them over and the ones in the air stop existing.
			if (flight.Progress < 1.0f)
				continue;

			flight.Shown += flight.Amount;
			flight.Amount = 0;

			// Handed over as CHIPS. This is the whole reason a flight carries runs.
			flight.Resting.AddRange(flight.InAir);
			flight.InAir.Clear();
			flight.Pile.Clear();
		}

		// Only when something actually changed: this presenter redraws every seat's chips and
		// labels, and there is no reason to pay for that on a still table.
		if (moved)
			Refresh();
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
		// The two piles are not the same kind of object. A bet was thrown in, so it lies loose and
		// settles; a stack was built by its owner, so it is nearly straight.
		var bet = PileFor(_bets, playerId, ChipScatter, settles: true);
		var stack = PileFor(_stacks, playerId, StackScatter, settles: false);
		if (bet == null || stack == null)
			return;

		var turned = Basis.FromEuler(new Vector3(0.0f, PokerTableLayout.YawTowardCentre(facing), 0.0f));

		var betPlace = PokerTableLayout.SeatSpot(facing, spec.SeatBetRadius);
		var stackPlace = PokerTableLayout.SeatSpot(facing, spec.SeatStackRadius);

		var flight = FlightFor(playerId, turned);
		var realBet = _game.BetOf(playerId);

		if (flight != null)
		{
			if (realBet > flight.Shown && flight.Progress >= 1.0f)
			{
				// Chips do not appear at the bet — they are carried there. The bet pile keeps
				// drawing the old amount until they land, so nothing is ever in two places at once.
				flight.Amount = realBet - flight.Shown;
				flight.From = stackPlace;
				flight.To = betPlace;
				flight.Progress = 0.0f;

				// Decided ONCE, here, and then handed over intact when they land. This is what the
				// player asked for: the chips that go in are the chips that stay in. Deciding it
				// again at the bet re-decomposed the whole pile, and the chips they had just thrown
				// changed colour under their hand.
				flight.InAir.Clear();
				flight.InAir.AddRange(PokerChipStack.Decompose(flight.Amount, bet.MaxDenominations));

				flight.Pile.SetRuns(flight.InAir);

				// They leave the hand as a tidy stack and open up on the way down.
				flight.Pile.Spread = 0.0f;
			}
			else if (realBet < flight.Shown)
			{
				// The street was swept into the middle, or a new hand started.
				flight.Shown = realBet;
				flight.Amount = 0;
				flight.Progress = 1.0f;
				flight.InAir.Clear();
				flight.Resting.Clear();
				flight.Pile.Clear();
			}

			bet.Transform = new Transform3D(turned, new Vector3(betPlace.X, 0.0f, betPlace.Y));
			bet.SetRuns(flight.Resting);
			PlaceFlight(flight, turned);
		}

		stack.Transform = new Transform3D(turned, new Vector3(stackPlace.X, 0.0f, stackPlace.Y));
		stack.Show(_game.StackOf(playerId));
	}

	private ChipFlight FlightFor(string playerId, Basis turned)
	{
		if (_flights.TryGetValue(playerId, out var existing))
			return existing;

		// No settle of its own: this pile's whole trajectory IS the drop, and the bet pile it lands
		// in does the bounce.
		var pile = new PokerChipPile { Scatter = ChipScatter, Spread = 0.0f };
		AddChild(pile);

		var flight = new ChipFlight { Pile = pile };
		_flights[playerId] = flight;

		return flight;
	}

	private void PlaceFlight(ChipFlight flight, Basis turned)
	{
		if (flight.Progress >= 1.0f)
		{
			flight.Pile.Visible = false;
			return;
		}

		flight.Pile.Visible = true;
		flight.Pile.Transform = new Transform3D(turned, FlightPoint(flight));
	}

	/// <summary>
	/// Where the chips are along the throw.
	///
	/// They are CARRIED across first and only released once they are over the spot, then they fall
	/// with acceleration. That ordering is the whole difference between chips that read as dropped
	/// and chips that read as slid across the cloth.
	/// </summary>
	private Vector3 FlightPoint(ChipFlight flight)
	{
		const float RiseUntil = 0.45f;

		var t = flight.Progress;
		var flat = flight.From.Lerp(flight.To, SmoothStep(Mathf.Min(t / CarryUntil, 1.0f)));

		float height;
		if (t < RiseUntil)
			height = ChipFlightArc * SmoothStep(t / RiseUntil);
		else if (t < CarryUntil)
			height = ChipFlightArc;
		else
		{
			// Squared, so it accelerates like something let go rather than something lowered.
			var fall = (t - CarryUntil) / (1.0f - CarryUntil);
			height = ChipFlightArc * (1.0f - fall * fall);
		}

		return new Vector3(flat.X, Mathf.Max(height, 0.0f), flat.Y);
	}

	private static float SmoothStep(float t)
	{
		t = Mathf.Clamp(t, 0.0f, 1.0f);
		return t * t * (3.0f - 2.0f * t);
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
		label.Text = $"{NameOf(playerId)}\n{_game.StackOf(playerId)}{suffix}";
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
			Scatter = scatter,
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

		Drop(_flights, seen, flight => flight.Pile.QueueFree());
		Drop(_bets, seen, pile => pile.QueueFree());
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
