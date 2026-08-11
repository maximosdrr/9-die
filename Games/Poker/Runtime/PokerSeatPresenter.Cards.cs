using System.Collections.Generic;
using Godot;
using Poker.Rules;

/// <summary>Owns the lifecycle and table motion of each seat's two hole-card nodes.</summary>
public partial class PokerSeatPresenter : Node3D
{
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
            // Both local and remote pairs travel back to the cloth. Until a third-person rig exposes
            // a real card-release marker, remote cards start at a stable estimate in front of that
            // player's hands. Snapping them straight to the table made every opponent reveal flick.
            if (!wasHeldLocally)
            {
                for (var i = 0; i < hand.Cards.Length; i++)
                    hand.ReleasedFrom[i] = EstimatedHeldCardTransform(facing, i, spec);
            }
            hand.Revealed = true;
            hand.Returning = true;
            hand.Returned = 0.0f;
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
        // Hidden/dealt cards may face this peer for legibility. Once exposed, however, each pair
        // belongs to a physical seat and faces its OWNER, just as cards laid down by that player
        // would. This is the one table visual that intentionally is not reoriented per viewer.
        var readerYaw = ReaderYaw(facing);
        var readerTurned = Basis.FromEuler(new Vector3(0.0f, readerYaw, 0.0f));
        var deck = BoardPresenter.DeckPosition;

        if ((_showdownPresenter?.Active ?? false) && shown)
        {
            foreach (var source in hand.Cards)
                source.Visible = false;
            return;
        }

        if (_game.HandNumber > 0 && hand.Folded && !shown)
        {
            PlaceMuck(playerId, hand, facing, spec, readerYaw);
            return;
        }

        var taken = !shown && HasTakenCardsUp(playerId, hand);
        if (hand.InFirstPerson)
            return;

        for (var i = 0; i < hand.Cards.Length; i++)
        {
            var card = hand.Cards[i];
            var seat = Mathf.Max(System.Array.IndexOf(_game.SeatOrder, playerId), 0);
            var motionSeed = seat * PokerDeal.HoleCardCount + i;
            var target = shown
                ? RevealedCardTransform(facing, seat, i, spec)
                : DealtCardTransform(facing, i, spec, readerTurned);

            if (_game.HandNumber <= 0 || taken)
            {
                card.Visible = false;
                continue;
            }

            card.Visible = true;

            var seated = target.Origin;

            if (shown && hand.Returning)
            {
                var delay = i * 0.10f;
                var returned = PokerMotion.Smooth(Mathf.Clamp(
                    (hand.Returned - delay) / (1.0f - delay), 0.0f, 1.0f));
                var from = hand.ReleasedFrom[i].Origin;
                var returnPosition = PokerMotion.CardThrow(from, seated, returned, 0.035f,
                    PokerChipPile.Noise(i, 18) * 0.010f);
                var targetBasis = target.Basis;
                var basis = hand.ReleasedFrom[i]
                    .InterpolateWith(new Transform3D(targetBasis, seated), returned)
                    .Basis;
                card.Transform = new Transform3D(basis, returnPosition);
                continue;
            }

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
            card.Transform = shown
                ? target
                : new Transform3D(readerTurned * dealWobble * PokerCard.Orientation(true), position);
        }
    }

    private static Transform3D DealtCardTransform(
        Vector2 facing, int index, PokerLayoutSpec spec, Basis turned)
    {
        var place = PokerTableLayout.SeatCardPosition(facing, index, spec);
        return new Transform3D(turned * PokerCard.Orientation(true),
            new Vector3(place.X, spec.CardThickness * 0.5f, place.Y));
    }

    /// <summary>
    /// A face-up pair in front of its owner. Variation is derived from hand/seat/card rather than a
    /// runtime RNG, so host and clients see the same natural placement without networking transforms.
    /// The second card rests a fraction higher: overlapping paper has an unambiguous draw order and
    /// cannot z-fight even when the imported mesh face is slightly thicker than the logical card.
    /// </summary>
    private Transform3D RevealedCardTransform(
        Vector2 facing, int seat, int index, PokerLayoutSpec spec)
    {
        var direction = facing.Normalized();
        var across = new Vector2(-direction.Y, direction.X);
        var seed = (_game.HandNumber * Mathf.Max(1, _game.SeatOrder.Length) + seat)
            * PokerDeal.HoleCardCount + index;
        var lateral = (index - (PokerDeal.HoleCardCount - 1) * 0.5f)
            * Mathf.Max(0.0f, ShowdownPairSpacing);
        lateral += PokerChipPile.Noise(seed, 31) * Mathf.Max(0.0f, ShowdownPairPositionJitter);
        var radial = spec.SeatCardRadius
            + PokerChipPile.Noise(seed, 32) * Mathf.Max(0.0f, ShowdownPairPositionJitter);
        var place = direction * radial + across * lateral;
        var height = spec.CardThickness * 0.5f
            + index * Mathf.Max(spec.CardThickness, ShowdownPairLayerSeparation);
        var yaw = PokerTableLayout.YawTowardCentre(direction)
            + Mathf.DegToRad(PokerChipPile.Noise(seed, 33)
                * Mathf.Max(0.0f, ShowdownPairAngleJitterDegrees));

        return new Transform3D(
            new Basis(Vector3.Up, yaw) * PokerCard.Orientation(false),
            new Vector3(place.X, height, place.Y));
    }

    /// <summary>
    /// Temporary third-person release pose. It is deliberately isolated here so a future hand rig
    /// only has to supply this transform; none of the reveal timing or networking has to change.
    /// </summary>
    private Transform3D EstimatedHeldCardTransform(
        Vector2 facing, int index, PokerLayoutSpec spec)
    {
        var direction = facing.Normalized();
        var across = new Vector2(-direction.Y, direction.X);
        var held = direction * (spec.SeatCardRadius - 0.05f)
            + across * (index == 0 ? -0.028f : 0.028f);
        var yaw = PokerTableLayout.YawTowardCentre(direction)
            + Mathf.DegToRad(index == 0 ? -5.0f : 5.0f);
        return new Transform3D(
            new Basis(Vector3.Up, yaw) * PokerCard.Orientation(true),
            new Vector3(held.X, HandHeight + index * 0.004f, held.Y));
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
}
