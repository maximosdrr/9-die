using System.Collections.Generic;
using Godot;
using Poker.Rules;

/// <summary>
/// The community cards and the pot, in the middle of the cloth.
///
/// Cards are spawned locally on every peer and never replicated: their positions are a pure function
/// of the board, so a card being turned costs one int rather than a node spawn plus a transform.
/// A card's place is fixed by its INDEX, so turning the turn and the river never slides the flop.
///
/// All five are dealt face down at the start of the hand and turned over as the streets open, which
/// is both what a dealer does and what makes the reveal legible: a card that appears and flips reads
/// as a moment, where one that simply pops into existence face up reads as a glitch. The animation
/// is entirely local — the server says only how many are face up, and every peer arrives at the same
/// table by the same rules.
/// </summary>
[GlobalClass]
public partial class PokerBoardPresenter : Node3D
{
    [Export] public PackedScene CardScene;
    [Export] public PokerChipPile PotPile;

    [ExportGroup("Layout")]
    [Export] public float CardWidth = 0.070f;
    [Export] public float CardLength = 0.098f;
    [Export] public float CardThickness = 0.0006f;
    [Export] public float CardGap = 0.012f;
    [Export] public float BoardOffset = 0.0f;
    [Export] public float PotRadius = 0.16f;
    [Export] public float SeatCardRadius = 0.40f;
    [Export] public float SeatBetRadius = 0.25f;
    [Export] public float SeatStackRadius = 0.54f;

    [ExportGroup("Deck")]
    /// <summary>
    /// Where the deck sits, in the reader's own frame — off to one side of the community row, clear
    /// of every seat's chips. Everything dealt comes from here, so the cards have somewhere to come
    /// FROM instead of appearing out of the cloth.
    /// </summary>
    [Export] public Vector3 DeckOffset = new(0.30f, 0.0f, -0.15f);

    /// <summary>How many cards are drawn in the stack. Enough to read as a deck, not 52.</summary>
    [Export] public int DeckDepth = 8;

    /// <summary>
    /// Where thrown-away hands land, in the reader's own frame: the dealer's other side, opposite the
    /// deck, so the discards never pile up on the cards still to come.
    /// </summary>
    [Export] public Vector3 MuckOffset = new(-0.30f, 0.0f, -0.16f);

    [ExportGroup("Dealing")]
    /// <summary>Where a card slides in from. Zero means "from the deck", which is what it should be.</summary>
    [Export] public Vector3 DealOrigin = Vector3.Zero;

    /// <summary>Seconds for one card to travel to its place.</summary>
    [Export] public float DealSeconds = 0.55f;

    /// <summary>Gap between one card setting off and the next, so they land in order.</summary>
    [Export] public float DealStagger = 0.26f;

    /// <summary>Seconds for one card to turn over.</summary>
    [Export] public float FlipSeconds = 0.5f;

    /// <summary>
    /// Gap between neighbouring cards flipping. Longer than the flip itself, on purpose: at 0.14
    /// against a 0.42 flip all three flop cards were mid-turn at the same moment and read as one
    /// simultaneous event. A dealer turns them one, then the next, then the next.
    /// </summary>
    [Export] public float FlipStagger = 0.55f;

    /// <summary>How high a card rises as it turns, so it arcs instead of spinning flat.</summary>
    [Export] public float FlipLift = 0.030f;

    /// <summary>How the five cards are doing right now. Purely local presentation.</summary>
    private sealed class BoardCard
    {
        public PokerCard Node;
        public float Dealt;
        public float Flipped;
        public float Wait;
        public bool WantsFaceUp;
        public int ShownId = Poker.Rules.CardId.None;
    }

    private readonly List<BoardCard> _cards = new();
    private int _handNumber = -1;
    private int _faceUpCount;
    private int _allowedFaceUpCount = PokerDeal.BoardCount;
    private bool _presentationGateEnabled;
    private PokerGame _game;

    private Node3D _deck;

    /// <summary>How many community cards this peer is currently allowed to show.</summary>
    public int VisibleFaceUpCount => _faceUpCount;

    /// <summary>The persistent physical node for one community-card slot.</summary>
    public PokerCard BoardCardNodeAt(int index) =>
        index >= 0 && index < _cards.Count ? _cards[index].Node : null;

    public override void _Ready()
    {
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _game = GetParent<PokerGame>();
        if (PotPile != null && _game?.ChipScene != null)
            PotPile.ChipScene = _game.ChipScene;

        BuildDeck();
        SetProcess(false);
    }

    /// <summary>Where the deck lies, in this presenter's space. Anything dealt starts here.</summary>
    public Vector3 DeckPosition => new Basis(Vector3.Up, ReaderYaw()) * DeckOffset;

    /// <summary>Where thrown-away hands lie, in this presenter's space.</summary>
    public Vector3 MuckPosition => new Basis(Vector3.Up, ReaderYaw()) * MuckOffset;

    /// <summary>Where visually collected bets gather, in this presenter's local space.</summary>
    public Vector3 PotPosition
    {
        get
        {
            var spec = Spec;
            var reader = new Basis(Vector3.Up, ReaderYaw());
            // PotRadius is measured from the board centre and is deliberately independent of card
            // length. The former hard-coded 7 cm margin left the chip faces visually under the board.
            return reader * new Vector3(0.0f, 0.0f, spec.BoardOffset + spec.PotRadius);
        }
    }

    /// <summary>
    /// Keeps public board state immediate while letting the chip presentation decide when the next
    /// card may visibly turn. This is local-only and never delays rules or networking.
    /// </summary>
    public void EnablePresentationGate(PokerStreet visibleStreet)
    {
        _presentationGateEnabled = true;
        AllowBoardThrough(visibleStreet);
        PotPile?.Clear();
        if (PotPile != null)
            PotPile.Visible = false;
    }

    public void AllowBoardThrough(PokerStreet street)
    {
        _allowedFaceUpCount = PokerDeal.BoardSize(street);
        if (_game != null && _handNumber >= 0)
            Sync(_game.Board, _game.PotInMiddle, _game.HandNumber, _game.Street);
    }

    /// <summary>
    /// A short stack of backs. Cosmetic — the real deck is the server's and never leaves it — but it
    /// gives every dealt card an origin the player can see.
    /// </summary>
    private void BuildDeck()
    {
        if ((CardScene == null && _game?.VisualAssets?.CardScene == null) || _deck != null)
            return;

        _deck = new Node3D { Name = "Deck" };
        AddChild(_deck);

        var spec = Spec;

        for (var i = 0; i < Mathf.Max(1, DeckDepth); i++)
        {
            var card = _game?.CreateCard(CardScene);
            if (card == null)
                break;

            _deck.AddChild(card);
            card.Configure(0, spec, faceDown: true);

            // Turned over, because Configure only records that a card is face down — the caller has
            // to actually rotate it. Left flat it showed the blank placeholder face, which is the
            // white rectangle that appeared in the middle of the table.
            // A hair of jitter on top, so the stack reads as paper rather than one extruded block.
            card.Transform = new Transform3D(
                new Basis(Vector3.Up, (i % 2 == 0 ? 1.0f : -1.0f) * 0.012f) * PokerCard.Orientation(true),
                new Vector3(0.0f, (i + 0.5f) * spec.CardThickness * 1.6f, 0.0f));
        }
    }

    private void PlaceDeck()
    {
        if (_deck == null)
            return;

        _deck.Transform = new Transform3D(new Basis(Vector3.Up, ReaderYaw()), DeckPosition);
    }

    /// <summary>
    /// Which way up the row is drawn, so it reads from THIS player's chair.
    ///
    /// Community cards cannot face four seats at once, and nothing about them is replicated — each
    /// peer builds its own table from the same public state — so each peer may as well turn them
    /// toward its own player rather than making three of the four crane.
    /// </summary>
    private float ReaderYaw()
    {
        var playerId = _game?.Player == null ? null : (string)_game.Player.Name;
        var seat = playerId == null ? null : _game.SeatFor(playerId);
        if (seat == null)
            return 0.0f;

        var toSeat = ToLocal(seat.GlobalPosition);
        var facing = new Vector2(toSeat.X, toSeat.Z);

        return facing.LengthSquared() < 1e-6f
            ? 0.0f
            : PokerTableLayout.YawTowardCentre(facing.Normalized());
    }

    /// <summary>One source for every measurement on this table.</summary>
    public PokerLayoutSpec Spec => new(
        CardWidth, CardLength, CardThickness, CardGap,
        BoardOffset, PotRadius, SeatCardRadius, SeatBetRadius, SeatStackRadius);

    /// <summary>True once every card has finished arriving and turning — what a test can wait on.</summary>
    public bool Settled
    {
        get
        {
            foreach (var card in _cards)
            {
                if (card.Node.Visible && (card.Dealt < 1.0f || !Mathf.IsEqualApprox(card.Flipped, card.WantsFaceUp ? 1.0f : 0.0f)))
                    return false;
            }

            return true;
        }
    }

    /// <summary>Per-card progress, for a test that needs to say WHY the row has not settled.</summary>
    public string DebugState()
    {
        var parts = new List<string>();
        foreach (var card in _cards)
        {
            parts.Add($"[vis={card.Node.Visible} espera={card.Wait:F2} entrou={card.Dealt:F2} "
                      + $"virou={card.Flipped:F2} quer={card.WantsFaceUp}]");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Takes the public board. <paramref name="board"/> holds only the cards the server has turned
    /// face up; the rest are laid out face down from the moment the hand is dealt.
    /// </summary>
    public void Sync(IReadOnlyList<int> board, int potTotal, int handNumber, PokerStreet street)
    {
        var shown = board?.Count ?? 0;
        var visibleShown = _presentationGateEnabled
            ? Mathf.Min(shown, _allowedFaceUpCount)
            : shown;
        var dealing = handNumber > 0;

        // A new hand takes the whole row off the table and deals it again.
        if (handNumber != _handNumber)
        {
            _handNumber = handNumber;
            _faceUpCount = 0;
            ResetRow(dealing);
        }

        for (var index = 0; index < PokerDeal.BoardCount; index++)
        {
            var card = CardAt(index);
            if (card == null)
                return;

            card.Node.Visible = dealing;
            if (!dealing)
                continue;

            // Configured with the real face as soon as the server sends it; the card is still shown
            // back-up until its flip runs, so nothing leaks — the id only arrives when it is public.
            var id = index < shown ? board[index] : Poker.Rules.CardId.None;
            if (id != card.ShownId)
            {
                card.ShownId = id;
                if (Poker.Rules.CardId.IsValid(id))
                    card.Node.Configure(id, Spec);
            }

            var wantsFaceUp = index < visibleShown;
            if (wantsFaceUp && !card.WantsFaceUp)
            {
                // Staggered from the first card of THIS street, so a flop turns over one by one and
                // a lone turn or river does not sit waiting for a queue that is not there.
                card.Wait = (index - _faceUpCount) * FlipStagger;
            }

            card.WantsFaceUp = wantsFaceUp;
        }

        _faceUpCount = visibleShown;
        PlaceDeck();
        PlaceAll();
        SetProcess(HasPendingCardMotion());

        if (PotPile == null)
            return;

        if (_presentationGateEnabled)
        {
            PotPile.Clear();
            PotPile.Visible = false;
            return;
        }

        var reader = new Basis(Vector3.Up, ReaderYaw());
        var potPlace = PotPosition;

        PotPile.Transform = new Transform3D(reader, potPlace);
        PotPile.Visible = true;

        // Grows by having the swept chips ADDED to it, so a pot that goes from 60 to 90 does not
        // re-decompose and repaint the chips already in the middle.
        PotPile.AddUpTo(potTotal);
    }

    public override void _Process(double delta)
    {
        var moved = false;

        foreach (var card in _cards)
        {
            if (!card.Node.Visible)
                continue;

            if (card.Wait > 0.0f)
            {
                card.Wait -= (float)delta;
                continue;
            }

            if (Advance(ref card.Dealt, 1.0f, DealSeconds, (float)delta))
                moved = true;

            // A card only turns over once it has arrived.
            if (card.Dealt >= 1.0f
                && Advance(ref card.Flipped, card.WantsFaceUp ? 1.0f : 0.0f, FlipSeconds, (float)delta))
                moved = true;
        }

        if (moved)
            PlaceAll();

        if (!HasPendingCardMotion())
            SetProcess(false);
    }

    private bool HasPendingCardMotion()
    {
        foreach (var card in _cards)
        {
            if (!card.Node.Visible)
                continue;

            if (card.Wait > 0.0f
                || card.Dealt < 1.0f
                || !Mathf.IsEqualApprox(card.Flipped, card.WantsFaceUp ? 1.0f : 0.0f))
                return true;
        }

        return false;
    }

    private static bool Advance(ref float value, float target, float seconds, float delta)
    {
        if (Mathf.IsEqualApprox(value, target))
        {
            // SNAP. Stopping at "approximately there" leaves the value a hair short, and everything
            // downstream compares exactly: Settled asks Dealt < 1, and the flip is gated behind
            // Dealt >= 1. A card resting at 0.9999999 therefore never settled and never turned over —
            // invisible until a change in speed happened to land the accumulation inside the epsilon.
            value = target;
            return false;
        }

        var step = seconds <= 0.0f ? 1.0f : delta / seconds;
        value = Mathf.MoveToward(value, target, step);
        return true;
    }

    private void PlaceAll()
    {
        var spec = Spec;
        var reader = new Basis(Vector3.Up, ReaderYaw());

        for (var index = 0; index < _cards.Count; index++)
        {
            var card = _cards[index];
            if (!card.Node.Visible)
                continue;

            var place = PokerTableLayout.BoardPosition(index, spec);

            // The ROW is turned as well as the cards. Turning only the cards left the row running
            // along the table's own X, so a player sitting on that axis saw five cards receding into
            // the distance instead of laid out across their view.
            var seated = reader * new Vector3(place.X, spec.CardThickness * 0.5f, place.Y);

            var from = DealOrigin.IsZeroApprox() ? DeckPosition : reader * DealOrigin;
            var sideways = PokerChipPile.Noise(index, 10) * 0.009f;
            var position = PokerMotion.CardThrow(from, seated, card.Dealt, 0.032f, sideways);

            // The turn is a half revolution about the card's own long axis — the sideways motion a
            // dealer uses — lifted through the middle so it arcs rather than grinding on the cloth.
            var turn = Mathf.Pi * (1.0f - Smooth(card.Flipped));
            position.Y += Mathf.Sin(Smooth(card.Flipped) * Mathf.Pi) * FlipLift;

            // A small launch wobble decays completely before contact. It keeps five cards from looking
            // like copies following the same rail while preserving their exact final alignment.
            var airborne = 1.0f - PokerMotion.Smooth(card.Dealt);
            var dealBasis = new Basis(Vector3.Up, PokerChipPile.Noise(index, 11) * 0.18f * airborne)
                            * new Basis(Vector3.Forward, PokerChipPile.Noise(index, 12) * 0.09f * airborne);
            card.Node.Transform = new Transform3D(reader * dealBasis * new Basis(Vector3.Back, turn), position);
        }
    }

    /// <summary>Smoothstep: the flip starts and finishes gently, which is what reads as natural.</summary>
    private static float Smooth(float t) => t * t * (3.0f - 2.0f * t);

    private void ResetRow(bool dealing)
    {
        for (var index = 0; index < PokerDeal.BoardCount; index++)
        {
            var card = CardAt(index);
            if (card == null)
                return;

            card.Dealt = 0.0f;
            card.Flipped = 0.0f;
            card.WantsFaceUp = false;
            card.ShownId = Poker.Rules.CardId.None;
            card.Wait = dealing ? index * DealStagger : 0.0f;
            card.Node.Configure(0, Spec, faceDown: true);
        }
    }

    public void Clear()
    {
        foreach (var card in _cards)
            card.Node.Visible = false;

        _handNumber = -1;
        _faceUpCount = 0;
        PotPile?.Clear();
    }

    private BoardCard CardAt(int index)
    {
        while (_cards.Count <= index)
        {
            var node = _game?.CreateCard(CardScene);
            if (node == null)
                return null;

            AddChild(node);
            _cards.Add(new BoardCard { Node = node });
        }

        return _cards[index];
    }
}
