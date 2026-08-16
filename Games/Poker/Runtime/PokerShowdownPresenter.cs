using System;
using System.Collections.Generic;
using Godot;
using Poker.Rules;

/// <summary>Owns the complete exposed-cards -> ranked best-five comparison presentation.</summary>
[GlobalClass]
public partial class PokerShowdownPresenter : Node3D
{
    private sealed class CardMove
    {
        public PokerCard Card;
        public Transform3D From;
        public Transform3D To;
        public float Delay;
        public bool FaceDown;
    }

    private readonly List<PokerCard> _cardPool = new();
    private readonly List<Label3D> _labels = new();
    private readonly List<Color> _labelColors = new();
    private readonly List<CardMove> _moves = new();
    private readonly List<string> _displayOrder = new();
    private PokerGame _game;
    private PokerBoardPresenter _board;
    private PokerPresentationProfile _profile;
    private Func<string, int, Transform3D> _sourceOf;
    private Func<string, string> _nameOf;
    private Action<IReadOnlyList<string>> _hideSources;
    private float _rowSpacing;
    private float _cardSpacing;
    private float _arc;
    private Color _winnerColor;
    private Color _otherColor;
    private int _hand = -1;
    private float _elapsed;
    private float _readingElapsed;
    private readonly List<CardMove> _cleanupMoves = new();
    private bool _cleaningUp;
    private float _cleanupElapsed;
    private float _cleanupReturnSeconds;
    private float _cleanupReturnEnd;
    private float _cleanupShuffleSeconds;

    public bool Active { get; private set; }
    public bool Settled { get; private set; }
    public bool ReadyForPayout { get; private set; }
    public float RevealHoldElapsed { get; private set; }
    public IReadOnlyList<string> DisplayOrder => _displayOrder;
    public int DisplayedCardCount => _moves.Count;
    public int CleanupCardCount => _cleanupMoves.Count;
    public int VisibleCardCount
    {
        get
        {
            var count = 0;
            foreach (var card in _cardPool)
            {
                if (card.Visible)
                    count++;
            }
            return count;
        }
    }

    public void Configure(
        PokerGame game, PokerBoardPresenter board, PackedScene cardScene,
        PokerPresentationProfile profile, Func<string, int, Transform3D> sourceOf,
        Func<string, string> nameOf, Action<IReadOnlyList<string>> hideSources,
        float rowSpacing, float cardSpacing, float arc, Color winnerColor, Color otherColor)
    {
        _game = game;
        _board = board;
        _profile = profile ?? new PokerPresentationProfile();
        _sourceOf = sourceOf;
        _nameOf = nameOf;
        _hideSources = hideSources;
        _rowSpacing = rowSpacing;
        _cardSpacing = cardSpacing;
        _arc = arc;
        _winnerColor = winnerColor;
        _otherColor = otherColor;
        BuildPool(cardScene);
    }

    public void Reset(bool authoritativeSettled = false, int hand = -1)
    {
        Active = false;
        Settled = authoritativeSettled;
        ReadyForPayout = authoritativeSettled;
        _hand = authoritativeSettled ? hand : -1;
        _elapsed = 0.0f;
        _readingElapsed = 0.0f;
        RevealHoldElapsed = 0.0f;
        _cleaningUp = false;
        _cleanupMoves.Clear();
        _moves.Clear();
        _displayOrder.Clear();
        foreach (var card in _cardPool)
            card.Visible = false;
        foreach (var label in _labels)
            label.Visible = false;
    }

    public bool Advance(float delta, bool blocked)
    {
        if (_cleaningUp)
            return AdvanceCardCleanup(delta);

        if (!Active)
            return TryStart(delta, blocked);
        if (Settled)
        {
            if (ReadyForPayout)
                return false;
            _readingElapsed += Mathf.Max(0.0f, delta);
            ReadyForPayout = _readingElapsed >= Mathf.Max(
                0.0f, _profile.RankedHandsReadingSeconds);
            return true;
        }

        _elapsed += delta;
        var allSettled = true;
        for (var index = 0; index < _moves.Count; index++)
        {
            var move = _moves[index];
            var raw = (_elapsed - move.Delay) / Mathf.Max(_profile.ShowdownCardSeconds, 0.01f);
            var t = Mathf.Clamp(raw, 0.0f, 1.0f);
            if (t < 1.0f)
                allSettled = false;
            var position = PokerMotion.CardThrow(move.From.Origin, move.To.Origin, t, _arc,
                PokerChipPile.Noise(index, 24) * 0.010f);
            var transform = move.From.InterpolateWith(move.To, PokerMotion.Smooth(t));
            move.Card.Transform = new Transform3D(transform.Basis, position);
        }

        for (var row = 0; row < _displayOrder.Count && row < _labels.Count; row++)
        {
            var delay = row * _profile.ShowdownRowStagger
                + _profile.ShowdownCardSeconds * 0.72f;
            var alpha = PokerMotion.Smooth(Mathf.Clamp((_elapsed - delay) / 0.24f, 0.0f, 1.0f));
            var color = _labelColors[row];
            color.A *= alpha;
            _labels[row].Modulate = color;
        }
        if (allSettled)
        {
            Settled = true;
            _readingElapsed = 0.0f;
            ReadyForPayout = _profile.RankedHandsReadingSeconds <= 0.0f;
        }
        return true;
    }

    public void BeginCardCleanup(
        float returnSeconds, float stagger, float shuffleSeconds, int startSlot = 0)
    {
        if (_cleaningUp)
            return;

        _cleaningUp = true;
        _cleanupElapsed = 0.0f;
        _cleanupReturnSeconds = Mathf.Max(0.01f, returnSeconds);
        var visibleCount = VisibleCardCount;
        _cleanupReturnEnd = _cleanupReturnSeconds
            + Mathf.Max(0, startSlot + visibleCount - 1) * Mathf.Max(0.0f, stagger);
        _cleanupShuffleSeconds = Mathf.Max(0.0f, shuffleSeconds);
        _cleanupMoves.Clear();

        var deckBasis = GlobalTransform.Basis.Inverse()
            * _board.GlobalTransform.Basis * _board.DeckCardBasis;
        var visibleIndex = 0;
        foreach (var card in _cardPool)
        {
            if (!card.Visible)
                continue;

            var slot = startSlot + visibleIndex;
            var target = new Transform3D(deckBasis,
                ToLocal(_board.ToGlobal(_board.CollectionTarget(slot))));
            _cleanupMoves.Add(new CardMove
            {
                Card = card,
                From = card.Transform,
                To = target,
                Delay = slot * Mathf.Max(0.0f, stagger),
                FaceDown = false,
            });
            visibleIndex++;
        }

        foreach (var label in _labels)
            label.Visible = false;
    }

    private bool AdvanceCardCleanup(float delta)
    {
        _cleanupElapsed += Mathf.Max(0.0f, delta);
        for (var index = 0; index < _cleanupMoves.Count; index++)
        {
            var move = _cleanupMoves[index];
            var t = Mathf.Clamp((_cleanupElapsed - move.Delay) / _cleanupReturnSeconds, 0.0f, 1.0f);
            if (t >= 1.0f && !move.FaceDown)
            {
                move.Card.Configure(0, _board.Spec, faceDown: true);
                move.FaceDown = true;
            }

            var position = PokerMotion.CardThrow(move.From.Origin, move.To.Origin, t, _arc,
                PokerChipPile.Noise(index, 63) * 0.010f);
            var transform = move.From.InterpolateWith(move.To, PokerMotion.Smooth(t));
            move.Card.Transform = new Transform3D(transform.Basis, position);
            if (t >= 1.0f && _board.CardCollectionComplete)
                move.Card.Visible = false;
        }

        if (_board.CardCleanupActive)
            return true;

        _cleaningUp = false;
        _cleanupMoves.Clear();
        Active = false;
        return true;
    }

    private bool TryStart(float delta, bool blocked)
    {
        if (_game == null || _board == null || _game.HandNumber <= 0 || _hand == _game.HandNumber
            || !_game.HandSettled || _game.RevealedHoleCards.Count == 0 || !_board.Settled || blocked)
        {
            RevealHoldElapsed = 0.0f;
            return false;
        }

        RevealHoldElapsed += Mathf.Max(0.0f, delta);
        if (RevealHoldElapsed < Mathf.Max(0.0f, _profile.ShowdownRevealHoldSeconds))
            return false;

        var ranked = new List<(string PlayerId, PokerHandRank Rank, int[] Cards)>();
        foreach (var entry in _game.RevealedHoleCards)
        {
            var cards = PokerHandEvaluator.BestFive(entry.Value, _game.Board);
            if (cards.Length == 5)
                ranked.Add((entry.Key, PokerHandEvaluator.Evaluate(entry.Value, _game.Board), cards));
        }
        ranked.Sort((left, right) =>
        {
            var strength = right.Rank.CompareTo(left.Rank);
            return strength != 0 ? strength : Array.IndexOf(_game.SeatOrder, left.PlayerId)
                .CompareTo(Array.IndexOf(_game.SeatOrder, right.PlayerId));
        });
        if (ranked.Count == 0 || ranked.Count * 5 > _cardPool.Count)
            return false;

        _hand = _game.HandNumber;
        Active = true;
        Settled = false;
        ReadyForPayout = false;
        _elapsed = 0.0f;
        _moves.Clear();
        _displayOrder.Clear();
        var spec = _board.Spec;
        var reader = new Basis(Vector3.Up, ReaderYaw());
        var rowCentre = (ranked.Count - 1) * 0.5f;
        var visualIndex = 0;
        for (var row = 0; row < ranked.Count; row++)
        {
            var result = ranked[row];
            _displayOrder.Add(result.PlayerId);
            var rowZ = (row - rowCentre) * _rowSpacing;
            var isWinner = _game.Winners.ContainsKey(result.PlayerId);
            for (var cardIndex = 0; cardIndex < result.Cards.Length; cardIndex++)
            {
                var cardId = result.Cards[cardIndex];
                var card = _cardPool[visualIndex];
                var source = _sourceOf?.Invoke(result.PlayerId, cardId)
                    ?? GlobalTransform.AffineInverse() * _board.GlobalTransform
                    * new Transform3D(
                        _board.DeckBasis * PokerCard.Orientation(false),
                        _board.DeckPosition);
                var targetPosition = reader * new Vector3((cardIndex - 2.0f) * _cardSpacing,
                    spec.CardThickness * 0.5f + 0.004f + row * 0.0005f, rowZ);
                var target = new Transform3D(reader * PokerCard.Orientation(false), targetPosition);
                card.Configure(cardId, spec);
                card.Transform = source;
                card.Visible = true;
                _moves.Add(new CardMove
                {
                    Card = card,
                    From = source,
                    To = target,
                    Delay = row * _profile.ShowdownRowStagger
                        + cardIndex * _profile.ShowdownCardStagger
                });
                visualIndex++;
            }

            var label = _labels[row];
            label.Text = isWinner
                ? $"VENCEDOR · {_nameOf?.Invoke(result.PlayerId) ?? result.PlayerId} — {result.Rank.Describe()}"
                : $"{row + 1}º · {_nameOf?.Invoke(result.PlayerId) ?? result.PlayerId} — {result.Rank.Describe()}";
            label.Position = reader * new Vector3(-0.31f, 0.030f, rowZ);
            label.Visible = true;
            _labelColors[row] = isWinner ? _winnerColor : _otherColor;
            label.Modulate = _labelColors[row] with { A = 0.0f };
        }

        for (var index = visualIndex; index < _cardPool.Count; index++)
            _cardPool[index].Visible = false;
        for (var row = ranked.Count; row < _labels.Count; row++)
            _labels[row].Visible = false;
        _hideSources?.Invoke(_displayOrder);
        return true;
    }

    private void BuildPool(PackedScene cardScene)
    {
        if (_cardPool.Count > 0)
            return;
        for (var i = 0; i < 20; i++)
        {
            var card = _game?.CreateCard(cardScene);
            if (card == null)
                break;
            card.Name = $"ShowdownCard{i}";
            card.Visible = false;
            AddChild(card);
            _cardPool.Add(card);
        }
        for (var i = 0; i < 4; i++)
        {
            var label = new Label3D
            {
                Name = $"ShowdownLabel{i}",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                PixelSize = 0.00020f,
                FontSize = 64,
                OutlineSize = 10,
                Visible = false
            };
            AddChild(label);
            _labels.Add(label);
            _labelColors.Add(_otherColor);
        }
    }

    private float ReaderYaw()
    {
        if (_board == null)
            return PokerTableLayout.YawTowardCentre(Vector2.Down);

        var boardFacing = _board.ReaderFacing;
        var worldFacing = _board.GlobalTransform.Basis
                          * new Vector3(boardFacing.X, 0.0f, boardFacing.Y);
        var localFacing = GlobalTransform.Basis.Inverse() * worldFacing;
        var reader = new Vector2(localFacing.X, localFacing.Z);
        return reader.LengthSquared() < 1e-6f
            ? PokerTableLayout.YawTowardCentre(Vector2.Down)
            : PokerTableLayout.YawTowardCentre(reader.Normalized());
    }
}
