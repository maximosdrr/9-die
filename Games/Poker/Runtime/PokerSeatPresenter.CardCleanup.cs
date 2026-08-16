using Godot;
using Poker.Rules;

/// <summary>Returns every persistent hole-card node to the deck between authoritative hands.</summary>
public partial class PokerSeatPresenter : Node3D
{
    private void BeginCardCleanup()
    {
        if (_cardCleanupActive)
            return;

        _cardCleanupActive = true;
        _cardCleanupElapsed = 0.0f;
        var spec = BoardPresenter.Spec;
        var boardCards = BoardPresenter.VisibleCardCount;
        var showdownCards = _showdownPresenter?.VisibleCardCount ?? 0;
        var slot = boardCards + showdownCards;
        var rankingOwnsSources = _showdownPresenter?.Active ?? false;

        foreach (var entry in _holeCards)
        {
            var hand = entry.Value;
            ReleaseCardsFromGrip(hand);
            var seat = SeatNodeFor(entry.Key);
            var local = seat == null ? Vector3.Zero : ToLocal(seat.GlobalPosition);
            var facing = new Vector2(local.X, local.Z);
            if (facing.LengthSquared() < 1e-6f)
                facing = Vector2.Down;
            else
                facing = facing.Normalized();

            for (var index = 0; index < hand.Cards.Length; index++)
            {
                var card = hand.Cards[index];
                if (!IsInstanceValid(card))
                {
                    hand.CleanupActive[index] = false;
                    continue;
                }
                var animate = card.Visible;

                // Opponents normally hold anonymous cards outside this presenter's tree view. Give
                // those cards a stable third-person source so they still travel instead of popping.
                if (!animate && !hand.Revealed && !rankingOwnsSources)
                {
                    card.Transform = EstimatedHeldCardTransform(facing, index, spec);
                    card.Visible = true;
                    animate = true;
                }

                hand.CleanupActive[index] = animate;
                hand.CleanupFaceDown[index] = false;
                hand.CleanupSlot[index] = slot;
                hand.CleanupFrom[index] = card.Transform;
                if (animate)
                    slot++;
            }
        }

        BoardPresenter.BeginCardCleanup(Profile.CardReturnSeconds,
            Profile.CardReturnStagger, Profile.DeckGatherHoldSeconds,
            Profile.DeckShuffleSeconds,
            totalCardSlots: Mathf.Max(1, slot), startSlot: 0);
        _showdownPresenter?.BeginCardCleanup(Profile.CardReturnSeconds,
            Profile.CardReturnStagger, Profile.DeckShuffleSeconds,
            startSlot: boardCards);
    }

    private bool AdvanceCardCleanup(float delta)
    {
        _cardCleanupElapsed += Mathf.Max(0.0f, delta);
        var duration = Mathf.Max(0.01f, Profile.CardReturnSeconds);
        var targetBasis = BoardBasisToPresenter(BoardPresenter.DeckCardBasis);
        var moved = false;

        foreach (var hand in _holeCards.Values)
        {
            for (var index = 0; index < hand.Cards.Length; index++)
            {
                if (!hand.CleanupActive[index])
                    continue;

                var card = hand.Cards[index];
                if (!IsInstanceValid(card))
                {
                    hand.CleanupActive[index] = false;
                    continue;
                }
                var delay = hand.CleanupSlot[index] * Mathf.Max(0.0f, Profile.CardReturnStagger);
                var t = Mathf.Clamp((_cardCleanupElapsed - delay) / duration, 0.0f, 1.0f);
                if (t >= 1.0f && !hand.CleanupFaceDown[index])
                {
                    card.Configure(0, BoardPresenter.Spec, faceDown: true);
                    hand.CleanupFaceDown[index] = true;
                }

                var target = BoardPositionToPresenter(
                    BoardPresenter.CollectionTarget(hand.CleanupSlot[index]));
                var position = PokerMotion.CardThrow(hand.CleanupFrom[index].Origin, target, t,
                    0.040f, PokerChipPile.Noise(hand.CleanupSlot[index], 62) * 0.010f);
                var basis = hand.CleanupFrom[index].InterpolateWith(
                    new Transform3D(targetBasis, target), PokerMotion.Smooth(t)).Basis;
                card.Transform = new Transform3D(basis, position);
                moved = true;

                if (t >= 1.0f && BoardPresenter.CardCollectionComplete)
                {
                    card.Visible = false;
                    hand.CleanupActive[index] = false;
                }
            }
        }

        return moved;
    }

    private void EndCardCleanup()
    {
        _cardCleanupActive = false;
        _cardCleanupElapsed = 0.0f;
        foreach (var hand in _holeCards.Values)
        {
            for (var index = 0; index < hand.Cards.Length; index++)
            {
                if (hand.CleanupActive[index] && IsInstanceValid(hand.Cards[index]))
                    hand.Cards[index].Visible = false;
                hand.CleanupActive[index] = false;
                hand.CleanupFaceDown[index] = false;
            }
        }
    }
}
