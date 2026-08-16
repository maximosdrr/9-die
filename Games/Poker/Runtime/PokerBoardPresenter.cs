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
/// All five are dealt face down at the start of the hand. As a street opens, its cards rise onto
/// their lower edge, hold upright facing the local reader, then lie face up again. The animation is
/// entirely local — the server says only how many are face up, and every peer arrives at the same
/// table by the same rules.
/// </summary>
[GlobalClass]
public partial class PokerBoardPresenter : Node3D
{
    [Export] public PackedScene CardScene;
    [Export] public PokerChipPile PotPile;
    [Export] public Node3D DeckAnchor;
    [Export] public Node3D CommunityCardsAnchor;

    [ExportGroup("Table surface")]
    /// <summary>
    /// Physical source of truth for the felt plane. Unlike this presenter's layout origin, the
    /// collider inherits every translation, rotation and scale applied to the actual table model.
    /// </summary>
    [Export] public CollisionShape3D TableSurfaceCollider;

    /// <summary>
    /// Visual fallback for tables whose collision shape is temporarily unavailable in an editor
    /// preview. Its highest point along local +Y is treated as the playable surface.
    /// </summary>
    [Export] public MeshInstance3D TableSurfaceMesh;

    [ExportGroup("Layout")]
    [Export] public float CardWidth = 0.076f;
    [Export] public float CardLength = 0.106f;
    [Export] public float CardThickness = 0.0006f;
    [Export] public float CardGap = 0.020f;
    [Export] public float BoardOffset = 0.0f;
    [Export] public float PotRadius = 0.16f;
    [Export] public float SeatCardRadius = 0.40f;
    [Export] public float SeatBetRadius = 0.32f;
    [Export] public float SeatStackRadius = 0.54f;

    /// <summary>
    /// Finds the real world-space felt plane near a point on the table. The layout root is only a
    /// coordinate frame and must not be used as a height reference: artists are free to resize the
    /// table without moving that root.
    /// </summary>
    public bool TryGetTableSurface(
        Vector3 nearWorldPosition,
        out Vector3 surfacePoint,
        out Vector3 surfaceNormal)
    {
        if (TryGetColliderTop(out var topPoint, out surfaceNormal))
        {
            surfacePoint = ProjectPointToPlane(nearWorldPosition, topPoint, surfaceNormal);
            return true;
        }

        if (TryGetMeshTop(nearWorldPosition, out surfacePoint, out surfaceNormal))
            return true;

        surfaceNormal = GlobalBasis.Y.Normalized();
        if (surfaceNormal.IsZeroApprox())
            surfaceNormal = Vector3.Up;
        surfacePoint = ProjectPointToPlane(nearWorldPosition, GlobalPosition, surfaceNormal);
        return false;
    }

    private bool TryGetColliderTop(out Vector3 topPoint, out Vector3 normal)
    {
        topPoint = Vector3.Zero;
        normal = Vector3.Up;
        if (!GodotObject.IsInstanceValid(TableSurfaceCollider)
            || TableSurfaceCollider.Shape == null)
            return false;

        var halfHeight = TableSurfaceCollider.Shape switch
        {
            CylinderShape3D cylinder => cylinder.Height * 0.5f,
            BoxShape3D box => box.Size.Y * 0.5f,
            CapsuleShape3D capsule => capsule.Height * 0.5f,
            SphereShape3D sphere => sphere.Radius,
            _ => -1.0f,
        };
        if (halfHeight < 0.0f)
            return false;

        normal = (TableSurfaceCollider.GlobalBasis.Inverse().Transposed()
                  * Vector3.Up).Normalized();
        if (normal.IsZeroApprox())
            normal = Vector3.Up;
        topPoint = TableSurfaceCollider.GlobalTransform
                   * new Vector3(0.0f, halfHeight, 0.0f);
        return true;
    }

    private bool TryGetMeshTop(
        Vector3 nearWorldPosition,
        out Vector3 surfacePoint,
        out Vector3 normal)
    {
        surfacePoint = Vector3.Zero;
        normal = Vector3.Up;
        if (!GodotObject.IsInstanceValid(TableSurfaceMesh) || TableSurfaceMesh.Mesh == null)
            return false;

        normal = (TableSurfaceMesh.GlobalBasis.Inverse().Transposed()
                  * Vector3.Up).Normalized();
        if (normal.IsZeroApprox())
            normal = Vector3.Up;

        var bounds = TableSurfaceMesh.Mesh.GetAabb();
        var end = bounds.End;
        var topProjection = float.NegativeInfinity;
        for (var x = 0; x < 2; x++)
        for (var y = 0; y < 2; y++)
        for (var z = 0; z < 2; z++)
        {
            var corner = new Vector3(
                x == 0 ? bounds.Position.X : end.X,
                y == 0 ? bounds.Position.Y : end.Y,
                z == 0 ? bounds.Position.Z : end.Z);
            var projection = (TableSurfaceMesh.GlobalTransform * corner).Dot(normal);
            topProjection = Mathf.Max(topProjection, projection);
        }

        surfacePoint = nearWorldPosition
                       + normal * (topProjection - nearWorldPosition.Dot(normal));
        return float.IsFinite(topProjection);
    }

    private static Vector3 ProjectPointToPlane(
        Vector3 point,
        Vector3 pointOnPlane,
        Vector3 normal) =>
        point + normal * (pointOnPlane - point).Dot(normal);

    /// <summary>
    /// Only the five shared cards receive this readability scale. Hole cards and the camera-held
    /// hand keep their natural size, so improving the board cannot crowd the bottom of the screen.
    /// Positions still come from <see cref="Spec"/> and therefore remain deterministic on every peer.
    /// </summary>
    [Export(PropertyHint.Range, "1.0,1.25,0.01")] public float CommunityCardVisualScale = 1.20f;

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
    /// Small in-place separation used by the riffle. Both packets remain well inside one card's
    /// footprint; the former 34 mm offset produced two 68 mm-apart piles and read as a duplicated
    /// deck rather than one deck being shuffled.
    /// </summary>
    [Export] public float ShuffleSplitDistance = 0.010f;
    [Export] public float ShuffleLift = 0.014f;
    [Export(PropertyHint.Range, "0,12,0.25")] public float ShuffleHalfYawDegrees = 5.0f;

    /// <summary>
    /// Where thrown-away hands land, in the reader's own frame. Keep the muck on the near half of
    /// the table and slightly to the side: a folded pair should look like a short toss, while still
    /// clearing the pot, the player's wager and the chalk controls.
    /// </summary>
    [Export] public Vector3 MuckOffset = new(0.12f, 0.0f, 0.24f);

    [ExportGroup("Dealing")]
    /// <summary>Where a card slides in from. Zero means "from the deck", which is what it should be.</summary>
    [Export] public Vector3 DealOrigin = Vector3.Zero;

    /// <summary>Seconds for one card to travel to its place.</summary>
    [Export] public float DealSeconds = 0.55f;

    /// <summary>Gap between one card setting off and the next, so they land in order.</summary>
    [Export] public float DealStagger = 0.26f;

    /// <summary>Seconds for each rotating half of the reveal: table-to-upright and upright-to-table.</summary>
    [Export] public float FlipSeconds = 0.5f;

    /// <summary>Seconds for the complete upright turn around the card's own vertical axis.</summary>
    [Export] public float VerticalRevealSpinSeconds = 1.60f;

    /// <summary>How long the newly revealed street remains upright and readable after the turn.</summary>
    [Export] public float VerticalRevealHoldSeconds = 2.0f;

    /// <summary>
    /// Clearance between the lowest visible point and the physical felt. This is deliberately tiny:
    /// the card must look supported by its edge while still avoiding depth flicker.
    /// </summary>
    [Export] public float RevealSurfaceClearance = 0.00035f;

    /// <summary>How the five cards are doing right now. Purely local presentation.</summary>
    private sealed class BoardCard
    {
        public PokerCard Node;
        public float Dealt;
        public float Flipped;
        public float Wait;
        public bool WantsFaceUp;
        public int ShownId = Poker.Rules.CardId.None;
        public Transform3D CleanupFrom;
        public float CleanupDelay;
        public int CleanupSlot;
        public bool CleanupFaceDown;
    }

    private readonly List<BoardCard> _cards = new();
    private int _handNumber = -1;
    private int _faceUpCount;
    private int _allowedFaceUpCount = PokerDeal.BoardCount;
    private bool _presentationGateEnabled;
    private PokerGame _game;

    private Node3D _deck;
    private readonly List<PokerCard> _deckCards = new();
    private readonly List<Transform3D> _deckCardRest = new();
    private bool _cleaningUp;
    private float _cleanupElapsed;
    private float _returnSeconds;
    private float _returnEnd;
    private float _gatherHoldSeconds;
    private float _shuffleSeconds;
    private Transform3D _deckRest;
    private bool _collectionFinished;
    private string _readerPlayerId = "";
    public bool CardCleanupActive => _cleaningUp;
    public bool CardCollectionComplete => _collectionFinished;
    private float ShuffleStart => _returnEnd + _gatherHoldSeconds;
    public bool DeckGatherHoldInProgress => _cleaningUp
        && _cleanupElapsed >= _returnEnd && _cleanupElapsed < ShuffleStart;
    public bool DeckShuffleInProgress => _cleaningUp && _cleanupElapsed >= ShuffleStart;
    public float DeckCardSpread
    {
        get
        {
            if (_deckCards.Count == 0)
                return 0.0f;
            var minimum = float.MaxValue;
            var maximum = float.MinValue;
            foreach (var card in _deckCards)
            {
                minimum = Mathf.Min(minimum, card.Position.X);
                maximum = Mathf.Max(maximum, card.Position.X);
            }
            return maximum - minimum;
        }
    }
    public int ReturningCardCount
    {
        get
        {
            var count = 0;
            foreach (var card in _cards)
            {
                if (_cleaningUp && card.Node.Visible)
                    count++;
            }
            return count;
        }
    }
    public int VisibleCardCount
    {
        get
        {
            var count = 0;
            foreach (var card in _cards)
            {
                if (card.Node.Visible)
                    count++;
            }
            return count;
        }
    }

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
    public Vector3 DeckPosition => DeckTransformFor(ReaderFacing).Origin;
    public Basis DeckBasis => DeckTransformFor(ReaderFacing).Basis;
    private Transform3D DeckLocalTransform => new(DeckBasis, DeckPosition);
    public Basis DeckCardBasis => DeckBasis * PokerCard.Orientation(true);
    public float DeckTopHeight => Mathf.Max(1, DeckDepth) * Spec.CardThickness * 1.6f;

    /// <summary>
    /// The authored marker is the Seat0 view of the deck. Each peer rotates that complete frame to
    /// its own reader, exactly like the community row and action guide. The deck therefore occupies
    /// one stable screen-relative place without replicating a different transform over the network.
    /// </summary>
    public Transform3D DeckTransformFor(Vector2 facing)
    {
        if (DeckAnchor == null)
        {
            var reader = new Basis(Vector3.Up, PokerTableLayout.YawTowardCentre(facing));
            return new Transform3D(reader, reader * DeckOffset);
        }

        var canonical = GlobalTransform.AffineInverse() * DeckAnchor.GlobalTransform;
        return PokerTableLayout.ReaderAlignedFrame(canonical, facing, Vector2.Down);
    }

    /// <summary>Successive collected cards land above, never through, the visible deck proxy.</summary>
    public Vector3 CollectionTarget(int slot) => DeckPosition + Vector3.Up
        * (DeckTopHeight + (Mathf.Max(0, slot) + 0.5f) * Spec.CardThickness * 1.7f);

    /// <summary>Where thrown-away hands lie, in this presenter's space.</summary>
    public Vector3 MuckPosition => ReaderBasis * MuckOffset;

    /// <summary>
    /// Direction from the table centre to this peer's chair. Board cards, deck, physical pot and the
    /// local interaction guide all consume this exact value, so a different seat cannot produce a
    /// slightly different version of the centre layout.
    /// </summary>
    public Vector2 ReaderFacing
    {
        get
        {
            var playerId = !string.IsNullOrEmpty(_readerPlayerId)
                ? _readerPlayerId
                : GodotObject.IsInstanceValid(_game?.Player)
                    ? (string)_game.Player.Name
                    : "";
            return ReaderFacingFor(playerId);
        }
    }

    /// <summary>
    /// Resolves a reader from the stable seat assignment. The first-person controller supplies its
    /// own player id, so a client does not briefly inherit Seat0 while PokerGame.Player is waiting
    /// for a replicated Player node.
    /// </summary>
    public Vector2 ReaderFacingFor(string playerId)
    {
        var seat = string.IsNullOrEmpty(playerId) ? null : _game?.SeatFor(playerId);
        if (seat == null)
            return Vector2.Down;

        var toSeat = ToLocal(seat.GlobalPosition);
        var facing = new Vector2(toSeat.X, toSeat.Z);
        return facing.LengthSquared() < 1e-6f ? Vector2.Down : facing.Normalized();
    }

    /// <summary>Chooses the player whose local camera reads this peer's table presentation.</summary>
    public void SetReaderPlayer(string playerId)
    {
        if (string.IsNullOrEmpty(playerId) || _readerPlayerId == playerId)
            return;

        _readerPlayerId = playerId;
        PlaceDeck();
        if (_cleaningUp)
            _deckRest = DeckLocalTransform;
        PlaceAll();
        if (PotPile != null)
            PotPile.Transform = new Transform3D(ReaderBasis, PotPosition);
    }

    public Basis ReaderBasis =>
        new(Vector3.Up, PokerTableLayout.YawTowardCentre(ReaderFacing));

    private Transform3D CommunityCardsFrame => CommunityCardsTransformFor(ReaderFacing);

    /// <summary>
    /// The community marker is authored from Seat0. Every peer rotates that authored frame to its
    /// own reader, keeping the five faces upright from every chair without networked transforms.
    /// </summary>
    public Transform3D CommunityCardsTransformFor(Vector2 facing)
    {
        if (CommunityCardsAnchor == null)
        {
            return new Transform3D(
                new Basis(Vector3.Up, PokerTableLayout.YawTowardCentre(facing)), Vector3.Zero);
        }

        var canonical = GlobalTransform.AffineInverse()
                        * CommunityCardsAnchor.GlobalTransform;
        return PokerTableLayout.ReaderAlignedFrame(canonical, facing, Vector2.Down);
    }

    /// <summary>
    /// Where visually collected bets gather, in this presenter's local space. It is always directly
    /// below the community row from the local player's view, at the position used before the new HUD.
    /// </summary>
    public Vector3 PotPosition
    {
        get
        {
            var distance = Spec.BoardOffset + Spec.PotRadius;
            return new Vector3(ReaderFacing.X * distance, 0.0f, ReaderFacing.Y * distance);
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
            _deckCards.Add(card);
            _deckCardRest.Add(card.Transform);
        }
    }

    private void PlaceDeck()
    {
        if (_deck == null)
            return;

        _deck.Transform = DeckLocalTransform;
    }

    /// <summary>
    /// Which way up the row is drawn, so it reads from THIS player's chair.
    ///
    /// Community cards cannot face four seats at once, and nothing about them is replicated — each
    /// peer builds its own table from the same public state — so each peer may as well turn them
    /// toward its own player rather than making three of the four crane.
    /// </summary>
    /// <summary>One source for every measurement on this table.</summary>
    public PokerLayoutSpec Spec => new(
        CardWidth, CardLength, CardThickness, CardGap,
        BoardOffset, PotRadius, SeatCardRadius, SeatBetRadius, SeatStackRadius);

    /// <summary>The board-only visual size; it never participates in rules or network state.</summary>
    public PokerLayoutSpec CommunityCardSpec
    {
        get
        {
            var scale = Mathf.Clamp(CommunityCardVisualScale, 1.0f, 1.25f);
            return new PokerLayoutSpec(
                CardWidth * scale, CardLength * scale, CardThickness, CardGap,
                BoardOffset, PotRadius, SeatCardRadius, SeatBetRadius, SeatStackRadius);
        }
    }

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
        if (_game?.CardsCleaningUp == true)
            return;

        if (_cleaningUp)
            EndCardCleanup();

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
                    card.Node.Configure(id, CommunityCardSpec);
            }

            var wantsFaceUp = index < visibleShown;
            if (wantsFaceUp && !card.WantsFaceUp)
            {
                // A street is one presentation beat. In particular, all three flop cards receive
                // the exact same clock: they rise, turn, hold and lie down in perfect synchrony.
                card.Wait = 0.0f;
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

        var reader = ReaderBasis;
        var potPlace = PotPosition;

        PotPile.Transform = new Transform3D(reader, potPlace);
        PotPile.Visible = true;

        // Grows by having the swept chips ADDED to it, so a pot that goes from 60 to 90 does not
        // re-decompose and repaint the chips already in the middle.
        PotPile.AddUpTo(potTotal);
    }

    public override void _Process(double delta)
    {
        if (_cleaningUp)
        {
            AdvanceCardCleanup((float)delta);
            return;
        }

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

            // A card only begins its raise/display/lay sequence once it has arrived.
            if (card.Dealt >= 1.0f
                && Advance(ref card.Flipped, card.WantsFaceUp ? 1.0f : 0.0f,
                    RevealDuration(), (float)delta))
                moved = true;
        }

        if (moved)
            PlaceAll();

        if (!HasPendingCardMotion())
            SetProcess(false);
    }

    public void BeginCardCleanup(
        float returnSeconds, float stagger, float gatherHoldSeconds, float shuffleSeconds,
        int totalCardSlots = 20, int startSlot = 0)
    {
        if (_cleaningUp)
            return;

        _cleaningUp = true;
        _collectionFinished = false;
        _cleanupElapsed = 0.0f;
        _returnSeconds = Mathf.Max(0.01f, returnSeconds);
        _returnEnd = _returnSeconds
            + Mathf.Max(0, totalCardSlots - 1) * Mathf.Max(0.0f, stagger);
        _gatherHoldSeconds = Mathf.Max(0.0f, gatherHoldSeconds);
        _shuffleSeconds = Mathf.Max(0.0f, shuffleSeconds);
        _deckRest = _deck != null ? DeckLocalTransform : Transform3D.Identity;
        for (var index = 0; index < _deckCards.Count; index++)
        {
            if (index < _deckCardRest.Count)
                _deckCardRest[index] = _deckCards[index].Transform;
        }

        var visibleIndex = 0;
        foreach (var card in _cards)
        {
            card.CleanupFrom = card.Node.Transform;
            card.CleanupDelay = (startSlot + visibleIndex) * Mathf.Max(0.0f, stagger);
            card.CleanupSlot = startSlot + visibleIndex;
            card.CleanupFaceDown = false;
            if (card.Node.Visible)
                visibleIndex++;
        }

        SetProcess(true);
    }

    private void AdvanceCardCleanup(float delta)
    {
        _cleanupElapsed += Mathf.Max(0.0f, delta);
        var targetBasis = DeckCardBasis;
        foreach (var card in _cards)
        {
            if (!card.Node.Visible)
                continue;

            var raw = (_cleanupElapsed - card.CleanupDelay) / _returnSeconds;
            var t = Mathf.Clamp(raw, 0.0f, 1.0f);
            if (t >= 1.0f && !card.CleanupFaceDown)
            {
                card.Node.Configure(0, CommunityCardSpec, faceDown: true);
                card.CleanupFaceDown = true;
            }

            var target = CollectionTarget(card.CleanupSlot);
            var position = PokerMotion.CardThrow(card.CleanupFrom.Origin, target, t, 0.040f,
                PokerChipPile.Noise(card.CleanupSlot, 61) * 0.010f);
            var basis = card.CleanupFrom.InterpolateWith(
                new Transform3D(targetBasis, target), PokerMotion.Smooth(t)).Basis;
            card.Node.Transform = new Transform3D(basis, position);
        }

        // Keep the complete pile visible for a short beat. This separates collecting from
        // shuffling instead of making the last card vanish as the deck starts moving.
        if (_cleanupElapsed >= ShuffleStart)
        {
            _collectionFinished = true;
            foreach (var card in _cards)
                card.Node.Visible = false;
        }

        if (_deck != null && _cleanupElapsed >= ShuffleStart && _shuffleSeconds > 0.0f)
        {
            var shuffle = Mathf.Clamp((_cleanupElapsed - ShuffleStart) / _shuffleSeconds, 0.0f, 1.0f);
            AnimateDetailedShuffle(shuffle);
        }

        if (_cleanupElapsed >= ShuffleStart + _shuffleSeconds)
            EndCardCleanup();
    }

    private void AnimateDetailedShuffle(float shuffle)
    {
        if (_deck == null || _deckCards.Count == 0)
            return;

        var halfCount = Mathf.Max(1, (_deckCards.Count + 1) / 2);
        // Loosen two overlapping packets, interleave, then square them. The packets never leave one
        // deck footprint, so reader-relative placement cannot look like a second physical deck.
        var split = Smooth(Mathf.Clamp(shuffle / 0.27f, 0.0f, 1.0f));
        for (var index = 0; index < _deckCards.Count; index++)
        {
            var source = _deckCardRest[index];
            var rightHalf = index >= halfCount;
            var within = rightHalf ? index - halfCount : index;
            var side = rightHalf ? 1.0f : -1.0f;
            var merge = Smooth(Mathf.Clamp(
                (shuffle - 0.35f - within * 0.040f) / 0.34f, 0.0f, 1.0f));
            var layer = Mathf.Min(_deckCards.Count - 1, within * 2 + (rightHalf ? 1 : 0));
            var target = new Transform3D(
                new Basis(Vector3.Up, (layer % 2 == 0 ? 1.0f : -1.0f) * 0.012f)
                * PokerCard.Orientation(true),
                new Vector3(0.0f, (layer + 0.5f) * Spec.CardThickness * 1.6f, 0.0f));

            var separated = source.Origin
                + Vector3.Right * (side * ShuffleSplitDistance * split)
                + Vector3.Back * ((within - (halfCount - 1) * 0.5f) * 0.0025f * split);
            var position = separated.Lerp(target.Origin, merge);
            position.Y += Mathf.Sin(merge * Mathf.Pi) * ShuffleLift;
            var splitBasis = source.Basis * new Basis(Vector3.Up,
                side * Mathf.DegToRad(ShuffleHalfYawDegrees) * split);
            var basis = new Transform3D(splitBasis, separated)
                .InterpolateWith(target, merge).Basis;
            _deckCards[index].Transform = new Transform3D(basis, position);
        }

        // One soft lateral press replaces the former double-frequency tap, which read as a twitch.
        var square = Mathf.Clamp((shuffle - 0.82f) / 0.18f, 0.0f, 1.0f);
        var tap = Mathf.Sin(square * Mathf.Pi * 2.0f) * (1.0f - square) * 0.003f;
        var across = ReaderBasis * Vector3.Right;
        _deck.Transform = new Transform3D(_deckRest.Basis, _deckRest.Origin + across * tap);
    }

    private void EndCardCleanup()
    {
        _cleaningUp = false;
        if (_deck != null)
        {
            _deckRest = DeckLocalTransform;
            _deck.Transform = _deckRest;
        }
        for (var index = 0; index < _deckCards.Count; index++)
        {
            var halfCount = Mathf.Max(1, (_deckCards.Count + 1) / 2);
            var rightHalf = index >= halfCount;
            var within = rightHalf ? index - halfCount : index;
            var layer = Mathf.Min(_deckCards.Count - 1, within * 2 + (rightHalf ? 1 : 0));
            var target = new Transform3D(
                new Basis(Vector3.Up, (layer % 2 == 0 ? 1.0f : -1.0f) * 0.012f)
                * PokerCard.Orientation(true),
                new Vector3(0.0f, (layer + 0.5f) * Spec.CardThickness * 1.6f, 0.0f));
            _deckCards[index].Transform = target;
            if (index < _deckCardRest.Count)
                _deckCardRest[index] = target;
        }
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
        var reader = ReaderBasis;
        var boardFrame = CommunityCardsFrame;
        // One shared orientation for the entire row. Computing a billboard from every card's own
        // position made their edges converge toward the eye like a fan; the reference presentation
        // is a single straight display plane facing the camera from the centre of the row.
        var rowCentre = boardFrame * new Vector3(
            0.0f, spec.CardThickness * 0.5f, spec.BoardOffset);
        var sharedFaceUpBasis = FaceUpBasisTowardCamera(rowCentre, boardFrame.Basis);

        for (var index = 0; index < _cards.Count; index++)
        {
            var card = _cards[index];
            if (!card.Node.Visible)
                continue;

            var place = PokerTableLayout.BoardPosition(index, spec);

            // The ROW is turned as well as the cards. Turning only the cards left the row running
            // along the table's own X, so a player sitting on that axis saw five cards receding into
            // the distance instead of laid out across their view.
            var seated = boardFrame * new Vector3(place.X, spec.CardThickness * 0.5f, place.Y);

            var from = DealOrigin.IsZeroApprox() ? DeckPosition : reader * DealOrigin;
            var sideways = PokerChipPile.Noise(index, 10) * 0.009f;
            var position = PokerMotion.CardThrow(from, seated, card.Dealt, 0.032f, sideways);

            // A small launch wobble decays completely before contact. It keeps five cards from looking
            // like copies following the same rail while preserving their exact final alignment.
            var airborne = 1.0f - PokerMotion.Smooth(card.Dealt);
            var dealBasis = new Basis(Vector3.Up, PokerChipPile.Noise(index, 11) * 0.18f * airborne)
                            * new Basis(Vector3.Forward, PokerChipPile.Noise(index, 12) * 0.09f * airborne);
            var basis = card.Dealt < 1.0f
                ? sharedFaceUpBasis * dealBasis * PokerCard.Orientation(true)
                : RevealBasis(card, sharedFaceUpBasis);
            card.Node.Transform = new Transform3D(basis, position);

            // Once it has reached the table, use the visible mesh — not the node origin or nominal
            // thickness — to keep the lowest corner tangent to the real felt. During the raise this
            // makes the card naturally roll onto its lower edge; in both flat states it prevents the
            // face from sinking into, or floating above, a resized/tilted table.
            if (card.Dealt >= 1.0f)
                RestVisibleGeometryOnTable(card.Node);
        }
    }

    private float RevealDuration() =>
        Mathf.Max(0.0f, FlipSeconds) * 2.0f
        + Mathf.Max(0.0f, VerticalRevealSpinSeconds)
        + Mathf.Max(0.0f, VerticalRevealHoldSeconds);

    /// <summary>
    /// Face down on the felt -> upright -> one complete vertical-axis turn -> readable hold -> face
    /// up on the felt. Every card introduced by the same street shares this exact clock, so the
    /// complete flop behaves as one straight tableau rather than three unrelated animations.
    /// </summary>
    private Basis RevealBasis(BoardCard card, Basis faceUpBasis)
    {
        var rotateSeconds = Mathf.Max(0.0f, FlipSeconds);
        var spinSeconds = Mathf.Max(0.0f, VerticalRevealSpinSeconds);
        var holdSeconds = Mathf.Max(0.0f, VerticalRevealHoldSeconds);
        var total = Mathf.Max(0.0001f, rotateSeconds * 2.0f + spinSeconds + holdSeconds);
        var elapsed = Mathf.Clamp(card.Flipped, 0.0f, 1.0f) * total;
        var faceDown = faceUpBasis * PokerCard.Orientation(true);
        var upright = faceUpBasis * new Basis(Vector3.Right, Mathf.Pi * 0.5f);

        if (rotateSeconds > 0.0f && elapsed < rotateSeconds)
            return BlendBasis(faceDown, upright, elapsed / rotateSeconds);

        if (spinSeconds > 0.0f && elapsed < rotateSeconds + spinSeconds)
        {
            var spin = Smoother((elapsed - rotateSeconds) / spinSeconds) * Mathf.Tau;
            // Upright local +Z points down into the supporting edge, so this is a true turn around
            // the card's vertical axis. A full revolution deliberately returns its face to camera.
            return upright * new Basis(Vector3.Back, spin);
        }

        if (elapsed <= rotateSeconds + spinSeconds + holdSeconds)
            return upright;

        if (rotateSeconds <= 0.0f)
            return faceUpBasis;

        return BlendBasis(upright, faceUpBasis,
            (elapsed - rotateSeconds - spinSeconds - holdSeconds) / rotateSeconds);
    }

    /// <summary>
    /// Builds a level card frame whose printed top points at this peer's camera position. Camera
    /// pitch/roll never tilts the table card: only the horizontal direction to the eye matters.
    /// When no runtime camera exists (headless tests/editor preview), the authored reader frame is
    /// already the same intended direction.
    /// </summary>
    private Basis FaceUpBasisTowardCamera(Vector3 localCardPosition, Basis fallback)
    {
        var cardWorld = ToGlobal(localCardPosition);
        TryGetTableSurface(cardWorld, out _, out var up);
        if (up.IsZeroApprox())
            up = Vector3.Up;
        up = up.Normalized();

        var toward = GodotObject.IsInstanceValid(_game?.Camera)
            ? _game.Camera.GlobalPosition - cardWorld
            : GlobalBasis * fallback.Z;
        toward -= up * toward.Dot(up);
        if (toward.IsZeroApprox())
        {
            toward = GlobalBasis * fallback.Z;
            toward -= up * toward.Dot(up);
        }
        if (toward.IsZeroApprox())
            toward = Vector3.Back;
        toward = toward.Normalized();

        var right = up.Cross(toward).Normalized();
        if (right.IsZeroApprox())
            right = Vector3.Right;
        var worldBasis = new Basis(right, up, toward).Orthonormalized();
        return (GlobalBasis.Inverse() * worldBasis).Orthonormalized();
    }

    private void RestVisibleGeometryOnTable(PokerCard card)
    {
        var near = card.GlobalPosition;
        TryGetTableSurface(near, out var surfacePoint, out var surfaceNormal);
        if (surfaceNormal.IsZeroApprox()
            || !card.TryGetVisibleProjectionRange(surfaceNormal, out var minimum, out _))
            return;

        surfaceNormal = surfaceNormal.Normalized();
        var desiredMinimum = surfacePoint.Dot(surfaceNormal)
                             + Mathf.Max(0.0f, RevealSurfaceClearance);
        card.GlobalPosition += surfaceNormal * (desiredMinimum - minimum);
    }

    private static Basis BlendBasis(Basis from, Basis to, float progress) =>
        new Transform3D(from, Vector3.Zero)
            .InterpolateWith(new Transform3D(to, Vector3.Zero), Smooth(progress))
            .Basis.Orthonormalized();

    /// <summary>Smoothstep: the flip starts and finishes gently, which is what reads as natural.</summary>
    private static float Smooth(float t) => t * t * (3.0f - 2.0f * t);

    /// <summary>Quintic ease for the long turn: zero velocity and acceleration at both ends.</summary>
    private static float Smoother(float t)
    {
        t = Mathf.Clamp(t, 0.0f, 1.0f);
        return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
    }

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
            card.Node.Configure(0, CommunityCardSpec, faceDown: true);
        }
    }

    public void Clear()
    {
        if (_cleaningUp)
            EndCardCleanup();
        foreach (var card in _cards)
            card.Node.Visible = false;

        _handNumber = -1;
        _faceUpCount = 0;
        _collectionFinished = false;
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
