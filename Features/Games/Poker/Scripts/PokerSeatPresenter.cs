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
	[Export] public float DealSeconds = 0.30f;

	/// <summary>Gap between cards leaving the deck, so they go round the table one at a time.</summary>
	[Export] public float DealStagger = 0.16f;

	/// <summary>How high a card arcs on the way over, so it is thrown rather than dragged.</summary>
	[Export] public float DealArc = 0.05f;

	/// <summary>
	/// How long an opponent's pair sits on the cloth before they take it up. The local player's
	/// stays until they pick it up themselves.
	/// </summary>
	[Export] public float OpponentPickUpDelay = 1.1f;

	/// <summary>A seat's two cards and how far along their deal is. Purely local presentation.</summary>
	private sealed class SeatHand
	{
		public readonly PokerCard[] Cards = new PokerCard[PokerDeal.HoleCardCount];
		public readonly float[] Dealt = new float[PokerDeal.HoleCardCount];
		public readonly float[] Wait = new float[PokerDeal.HoleCardCount];
		public int Hand = -1;
		public float OnTable;
		public bool Revealed;
	}

	private readonly Dictionary<string, SeatHand> _holeCards = new();
	private bool _lastPickedUp;

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
	}

	/// <summary>
	/// A seat's two cards: thrown from the deck at the start of the hand, lying face down until
	/// they are taken up, and back on the cloth face up at a showdown.
	///
	/// They do NOT sit here face down for the whole hand. Who is still in is already on the felt as
	/// a bet and a name; two blank rectangles per seat for the rest of the hand only crowded the
	/// table. What earns its place is the moment they arrive and the moment they are shown.
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

			var seat = System.Array.IndexOf(_game.SeatOrder, playerId);
			var seats = Mathf.Max(1, _game.SeatOrder.Length);

			for (var i = 0; i < hand.Cards.Length; i++)
			{
				hand.Dealt[i] = 0.0f;
				hand.Wait[i] = (i * seats + Mathf.Max(seat, 0)) * DealStagger;
				hand.Cards[i].Configure(0, spec, faceDown: true);
			}
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

		var isLocal = _game.Player != null && playerId == (string)_game.Player.Name;
		var taken = shown
			? false
			: isLocal
				? _game.LocalPickedUpCards
				: hand.OnTable > OpponentPickUpDelay;

		var folded = _game.HasFolded(playerId);

		for (var i = 0; i < hand.Cards.Length; i++)
		{
			var card = hand.Cards[i];

			if (_game.HandNumber <= 0 || taken || (folded && !shown))
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

		// Only when something actually changed: this presenter redraws every seat's chips and
		// labels, and there is no reason to pay for that on a still table.
		if (moved)
			Refresh();
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
		var bet = PileFor(_bets, playerId);
		var stack = PileFor(_stacks, playerId);
		if (bet == null || stack == null)
			return;

		var turned = Basis.FromEuler(new Vector3(0.0f, PokerTableLayout.YawTowardCentre(facing), 0.0f));

		var betPlace = PokerTableLayout.SeatSpot(facing, spec.SeatBetRadius);
		bet.Transform = new Transform3D(turned, new Vector3(betPlace.X, 0.0f, betPlace.Y));
		bet.Show(_game.BetOf(playerId));

		var stackPlace = PokerTableLayout.SeatSpot(facing, spec.SeatStackRadius);
		stack.Transform = new Transform3D(turned, new Vector3(stackPlace.X, 0.0f, stackPlace.Y));
		stack.Show(_game.StackOf(playerId));
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

	private PokerChipPile PileFor(Dictionary<string, PokerChipPile> into, string playerId)
	{
		if (into.TryGetValue(playerId, out var pile))
			return pile;

		pile = new PokerChipPile();
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
