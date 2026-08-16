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
            hand.PickUpAnimationSeconds = OpponentPickUpAnimationSeconds;
            hand.Revealed = false;
            hand.RevealPending = false;
            hand.RevealGestureStarted = false;
            hand.RevealElapsed = 0.0f;
            hand.RevealPreparationSeconds = 0.0f;
            hand.RevealReleaseSeconds = 0.0f;
            hand.RevealCutsceneRemaining = 0.0f;
            hand.Folded = false;
            hand.Mucked = 0.0f;
            hand.Returning = false;
            hand.Returned = 1.0f;

            var dealPosition = DealPosition(
                _game.SeatOrder, _game.ButtonSeat, playerId);
            var seats = Mathf.Max(1, _game.SeatOrder.Length);

            for (var i = 0; i < hand.Cards.Length; i++)
            {
                hand.Dealt[i] = 0.0f;
                hand.Wait[i] = (i * seats + dealPosition) * DealStagger;
                hand.ReleasedFrom[i] = Transform3D.Identity;
                hand.PickUpFrom[i] = Transform3D.Identity;
                hand.PickUpAttached[i] = false;
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
            hand.Revealed = true;
            hand.RevealPending = true;
            hand.RevealGestureStarted = false;
            hand.RevealElapsed = 0.0f;
            hand.RevealPreparationSeconds = 0.0f;
            hand.RevealReleaseSeconds = Mathf.Max(0.0f, ShowdownReleaseDelaySeconds);
            hand.RevealCutsceneRemaining = 0.0f;
            hand.Returning = false;
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

    private float StartShowdownGesture(string playerId)
    {
        if (PlayerRegistry.Instance is not { } registry || !registry.HasContainer())
            return 0.0f;

        var player = registry.GetPlayerById(playerId);
        return player?.PlaySeatedShowdownSequence(
            BoardPresenter.GlobalPosition,
            Mathf.Max(0.0f, ShowdownPreparationSeconds)) ?? 0.0f;
    }

    /// <summary>
    /// Detaches the same physical cards at the authored release frame. Until this seam, local cards
    /// follow the first-person left hand and remote cards follow CharacterVisual.CardGrip.
    /// </summary>
    private bool AdvancePendingShowdownReveal(string playerId, SeatHand hand, float delta)
    {
        if (!hand.RevealPending)
            return false;

        // The final call/all-in can publish its action and all revealed hands in one snapshot.
        // Do not let Showdown interrupt PokerBet or overtake the chips travelling to the pot.
        if (!hand.RevealGestureStarted)
        {
            if (!CanStartShowdownReveal())
                return false;

            hand.RevealGestureStarted = true;
            hand.RevealElapsed = 0.0f;
            PrepareCardsForShowdownGesture(playerId, hand);
            hand.RevealPreparationSeconds = StartShowdownGesture(playerId);
            hand.RevealReleaseSeconds = Mathf.Max(0.0f, ShowdownReleaseDelaySeconds)
                                        + hand.RevealPreparationSeconds;
            hand.RevealCutsceneRemaining = hand.RevealPreparationSeconds
                                           + PokerClips.ShowdownDurationSeconds;

            // Keep the seam observable for a full process frame even after a long hitch. The local
            // FP view runs later than the presenter and must see the started flag before release.
            return true;
        }

        hand.RevealElapsed += Mathf.Max(delta, 0.0f);
        if (hand.RevealElapsed < hand.RevealReleaseSeconds)
            return true;

        var seat = SeatNodeFor(playerId);
        var toSeat = seat != null ? ToLocal(seat.GlobalPosition) : Vector3.Zero;
        var facing = new Vector2(toSeat.X, toSeat.Z);
        if (facing.LengthSquared() < 1e-6f)
            facing = Vector2.Down;
        else
            facing = facing.Normalized();

        var wasHeld = CardsAreInGrip(hand);
        ReleaseCardsFromGrip(hand);
        if (!wasHeld)
        {
            var spec = BoardPresenter?.Spec ?? PokerLayoutSpec.Default;
            for (var index = 0; index < hand.Cards.Length; index++)
                hand.ReleasedFrom[index] = EstimatedHeldCardTransform(facing, index, spec);
        }

        hand.RevealPending = false;
        hand.RevealGestureStarted = false;
        hand.Returning = true;
        hand.Returned = 0.0f;
        return true;
    }

    /// <summary>
    /// Recovery is a state reconstruction, not a replay. Any hands already public in the snapshot
    /// are placed directly on the cloth and marked past their release seam.
    /// </summary>
    private void SnapRevealedHandsToAuthoritativeState(PokerLayoutSpec spec)
    {
        if (_game == null)
            return;

        foreach (var entry in _game.RevealedHoleCards)
        {
            var playerId = entry.Key;
            var hand = EnsureHand(playerId);
            if (hand == null)
                continue;

            ReleaseCardsFromGrip(hand);
            hand.Hand = _game.HandNumber;
            hand.Revealed = true;
            hand.RevealPending = false;
            hand.RevealGestureStarted = false;
            hand.RevealElapsed = hand.RevealReleaseSeconds;
            hand.RevealPreparationSeconds = 0.0f;
            hand.RevealCutsceneRemaining = 0.0f;
            hand.Returning = false;
            hand.Returned = 1.0f;
            hand.Folded = false;
            for (var index = 0; index < hand.Cards.Length; index++)
            {
                hand.Dealt[index] = 1.0f;
                hand.Wait[index] = 0.0f;
                if (index < entry.Value.Length)
                    hand.Cards[index].Configure(entry.Value[index], spec);
            }

            var seat = SeatNodeFor(playerId);
            var toSeat = seat != null ? ToLocal(seat.GlobalPosition) : Vector3.Zero;
            var facing = new Vector2(toSeat.X, toSeat.Z);
            if (facing.LengthSquared() < 1e-6f)
                facing = Vector2.Down;
            else
                facing = facing.Normalized();
            PlaceHoleCards(playerId, hand, facing, spec, shown: true);
        }
    }

    private bool CanStartShowdownReveal() =>
        _actionGestureRemaining <= 0.0f
        && PresentationReadyForAction;

    /// <summary>
    /// The all-in reveal has physically completed on this table: every shown pair has left the
    /// authored hands, landed on the real surface, and the Showdown clips have reached their end.
    /// The server uses this seam before it publishes awards and enables payout.
    /// </summary>
    public bool ShowdownCardsReadyForSettlement
    {
        get
        {
            if (_game == null || _game.RevealedHoleCards.Count == 0
                || !(BoardPresenter?.Settled ?? true) || !PresentationReadyForAction)
            {
                return false;
            }

            foreach (var playerId in _game.RevealedHoleCards.Keys)
            {
                if (!_holeCards.TryGetValue(playerId, out var hand)
                    || !hand.Revealed || hand.RevealPending || hand.Returning
                    || hand.RevealCutsceneRemaining > 0.0f)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>True once this player's authored throw has actually begun on this peer.</summary>
    public bool HasStartedShowdownGesture(string playerId) =>
        !string.IsNullOrEmpty(playerId)
        && _holeCards.TryGetValue(playerId, out var hand)
        && hand.Revealed
        && hand.RevealGestureStarted;

    /// <summary>The exact raised-pose lead-in chosen for this player's current reveal.</summary>
    public float ShowdownPreparationFor(string playerId) =>
        !string.IsNullOrEmpty(playerId)
        && _holeCards.TryGetValue(playerId, out var hand)
        && hand.RevealGestureStarted
            ? Mathf.Max(0.0f, hand.RevealPreparationSeconds)
            : 0.0f;

    /// <summary>
    /// Ensures the pair is in the authored starting pose before the Showdown clock begins. Usually
    /// it is already attached by the pickup flow; the estimate is a fallback for forced all-ins that
    /// reach showdown before any pickup view existed.
    /// </summary>
    private void PrepareCardsForShowdownGesture(string playerId, SeatHand hand)
    {
        if (CardsAreInGrip(hand))
            return;

        var localPlayerId = _game?.Player == null ? null : (string)_game.Player.Name;
        if (playerId != localPlayerId
            && PlayerRegistry.Instance != null
            && PlayerRegistry.Instance.TryGetPlayerById(playerId, out var player)
            && player.CharacterVisual?.CardGrip is { } grip)
        {
            var spec = BoardPresenter?.Spec ?? PokerLayoutSpec.Default;
            for (var index = 0; index < hand.Cards.Length; index++)
            {
                var card = hand.Cards[index];
                if (!IsInstanceValid(card))
                    continue;
                card.Reparent(grip, keepGlobalTransform: false);
                card.Transform = OpponentHeldCardTransform(index, spec);
                card.Visible = true;
                hand.PickUpAttached[index] = true;
            }
            return;
        }

        var seat = SeatNodeFor(playerId);
        var toSeat = seat != null ? ToLocal(seat.GlobalPosition) : Vector3.Zero;
        var facing = new Vector2(toSeat.X, toSeat.Z);
        facing = facing.LengthSquared() < 1e-6f ? Vector2.Down : facing.Normalized();
        var layout = BoardPresenter?.Spec ?? PokerLayoutSpec.Default;
        for (var index = 0; index < hand.Cards.Length; index++)
        {
            var card = hand.Cards[index];
            if (!IsInstanceValid(card))
                continue;
            if (card.GetParent() != this)
                card.Reparent(this, keepGlobalTransform: false);
            card.Transform = EstimatedHeldCardTransform(facing, index, layout);
            card.Visible = true;
        }
    }

    internal static int DealPosition(
        IReadOnlyList<string> seatOrder, int buttonSeat, string playerId)
    {
        var order = PokerSeating.DealOrder(seatOrder, buttonSeat);
        return Mathf.Max(order.IndexOf(playerId), 0);
    }

    private SeatHand EnsureHand(string playerId)
    {
        if (_holeCards.TryGetValue(playerId, out var existing))
            return RepairDisposedCards(existing) ? existing : null;

        var hand = new SeatHand();
        if (!RepairDisposedCards(hand))
            return null;

        _holeCards[playerId] = hand;
        return hand;
    }

    /// <summary>
    /// Hole cards are deliberately reparented into first- and third-person hand rigs. If one of
    /// those replaceable rigs is freed, Godot disposes its children while this presenter's stable
    /// seat dictionary still contains their C# wrappers. Repair the pair before any property is
    /// touched, and bring a surviving half back under this stable owner as well.
    /// </summary>
    private bool RepairDisposedCards(SeatHand hand)
    {
        var repaired = false;
        for (var i = 0; i < hand.Cards.Length; i++)
        {
            if (IsInstanceValid(hand.Cards[i]))
                continue;

            var card = _game?.CreateCard(CardScene);
            if (card == null)
                return false;

            AddChild(card);
            hand.Cards[i] = card;
            hand.Dealt[i] = 1.0f;
            hand.Wait[i] = 0.0f;
            hand.ReleasedFrom[i] = Transform3D.Identity;
            hand.PickUpFrom[i] = Transform3D.Identity;
            hand.PickUpAttached[i] = false;
            hand.CleanupActive[i] = false;
            hand.CleanupFaceDown[i] = false;
            repaired = true;
        }

        if (!repaired)
            return true;

        foreach (var card in hand.Cards)
        {
            if (IsInstanceValid(card) && card.GetParent() != this)
                card.Reparent(this, keepGlobalTransform: true);
        }
        hand.InFirstPerson = false;
        return true;
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
                if (!IsInstanceValid(card) || card.GetParent() == grip)
                    continue;

                card.Reparent(grip, keepGlobalTransform: true);
                card.Visible = true;
            }

            hand.InFirstPerson = true;
        }

        return hand.Cards;
    }

    /// <summary>
    /// Reparents each opponent's public face-down pair into the card marker authored with the body
    /// pose. The local player's real faces stay exclusively in their first-person grip.
    /// </summary>
    private void AttachOpponentCardsToCharacter(string playerId, SeatHand hand)
    {
        var localPlayerId = _game?.Player == null ? null : (string)_game.Player.Name;
        if (playerId == localPlayerId
            || PlayerRegistry.Instance == null
            || !PlayerRegistry.Instance.TryGetPlayerById(playerId, out var player)
            || player.CharacterVisual?.CardGrip == null)
        {
            return;
        }

        var grip = player.CharacterVisual.CardGrip;
        var duration = Mathf.Max(hand.PickUpAnimationSeconds, OpponentPickUpAnimationSeconds);
        var contact = OpponentPickUpDelay
                      + duration * Mathf.Clamp(OpponentPickUpContactFraction, 0.1f, 0.9f);
        var transferDuration = Mathf.Max(
            duration * (1.0f - Mathf.Clamp(OpponentPickUpContactFraction, 0.1f, 0.9f)),
            0.01f);
        var transfer = PokerMotion.Smooth(Mathf.Clamp(
            (hand.OnTable - contact) / transferDuration, 0.0f, 1.0f));
        for (var i = 0; i < hand.Cards.Length; i++)
        {
            var card = hand.Cards[i];
            if (!IsInstanceValid(card))
                continue;

            if (card.GetParent() != grip || !hand.PickUpAttached[i])
            {
                card.Reparent(grip, keepGlobalTransform: true);
                hand.PickUpFrom[i] = card.Transform;
                hand.PickUpAttached[i] = true;
            }

            var spec = _game.BoardPresenter?.Spec ?? PokerLayoutSpec.Default;
            if (card.CardId != 0 || !card.IsFaceDown)
                card.Configure(0, spec, faceDown: true);

            card.Transform = hand.PickUpFrom[i].InterpolateWith(
                OpponentHeldCardTransform(i, spec), transfer);
            card.Visible = true;
        }
    }

    private void StartOpponentCardPickup(string playerId, SeatHand hand)
    {
        var localPlayerId = _game?.Player == null ? null : (string)_game.Player.Name;
        if (playerId == localPlayerId
            || PlayerRegistry.Instance == null
            || !PlayerRegistry.Instance.TryGetPlayerById(playerId, out var player))
        {
            return;
        }

        var duration = player.CharacterVisual?.PlayCardPickupSequence(OpponentPickUpLookSeconds)
                       ?? 0.0;
        hand.PickUpAnimationSeconds = (float)Mathf.Max(duration, OpponentPickUpAnimationSeconds);
    }

    /// <summary>
    /// Public third-person pose for a face-down card in the authored left-hand grip.
    ///
    /// <see cref="HandFan.LongAxisUpFromMinusZ"/> points PokerCard's face toward the room. Rotate
    /// around the card's own long axis so the face points back at its owner while keeping the card
    /// upright. The tiny depth layer is deliberate: fanning only in X/Y leaves the two physical
    /// meshes intersecting and makes their backs flicker as the animated hand moves.
    /// </summary>
    internal static Transform3D OpponentHeldCardTransform(int index, PokerLayoutSpec spec)
    {
        var lateral = (index - (PokerDeal.HoleCardCount - 1) * 0.5f)
                      * spec.CardWidth * 0.42f;
        var fan = Mathf.DegToRad(index == 0 ? -7.0f : 7.0f);
        var faceAwayFromObservers = HandFan.LongAxisUpFromMinusZ
                                    * new Basis(Vector3.Back, Mathf.Pi);
        var depthLayer = index * Mathf.Max(spec.CardThickness * 2.0f, 0.0015f);
        return new Transform3D(
            new Basis(Vector3.Forward, fan) * faceAwayFromObservers,
            new Vector3(lateral, 0.0f, depthLayer));
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
        if (!CardsAreInGrip(hand))
            return;

        for (var i = 0; i < hand.Cards.Length; i++)
        {
            var card = hand.Cards[i];
            if (!IsInstanceValid(card))
                continue;

            if (card.GetParent() != this)
                card.Reparent(this, keepGlobalTransform: true);

            hand.ReleasedFrom[i] = card.Transform;
            hand.PickUpAttached[i] = false;
        }

        hand.InFirstPerson = false;
    }

    private bool CardsAreInGrip(SeatHand hand)
    {
        if (hand.InFirstPerson)
            return true;

        foreach (var card in hand.Cards)
        {
            if (IsInstanceValid(card) && card.GetParent() != this)
                return true;
        }

        return false;
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
        var deck = BoardPositionToPresenter(BoardPresenter.DeckPosition);

        // Showdown is now a physical release: keep the same nodes parented to the animated grip
        // until the authored fingers open, rather than teleporting them to the cloth at snapshot time.
        if (shown && hand.RevealPending)
            return;

        if (_game.HandNumber > 0 && hand.Folded && !shown)
        {
            PlaceMuck(playerId, hand, facing, spec, readerYaw);
            return;
        }

        var taken = !shown && HasTakenCardsUp(playerId, hand);
        if (hand.InFirstPerson)
            return;

        if (taken && !shown && !hand.Folded)
        {
            AttachOpponentCardsToCharacter(playerId, hand);
            return;
        }

        for (var i = 0; i < hand.Cards.Length; i++)
        {
            var card = hand.Cards[i];
            if (!IsInstanceValid(card))
                continue;

            if (card.GetParent() != this)
                card.Reparent(this, keepGlobalTransform: true);
            var seat = Mathf.Max(System.Array.IndexOf(_game.SeatOrder, playerId), 0);
            var motionSeed = seat * PokerDeal.HoleCardCount + i;
            var target = shown
                ? RevealedCardTransform(card, facing, seat, i, spec)
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
            new Vector3(place.X, PokerTableLayout.SeatCardHeight(index, spec), place.Y));
    }

    /// <summary>
    /// A face-up pair in front of its owner. Variation is derived from hand/seat/card rather than a
    /// runtime RNG, so host and clients see the same natural placement without networking transforms.
    /// The second card rests a fraction higher: overlapping paper has an unambiguous draw order and
    /// cannot z-fight even when the imported mesh face is slightly thicker than the logical card.
    /// </summary>
    private Transform3D RevealedCardTransform(
        PokerCard card, Vector2 facing, int seat, int index, PokerLayoutSpec spec)
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
        var yaw = PokerTableLayout.YawTowardCentre(direction)
            + Mathf.DegToRad(PokerChipPile.Noise(seed, 33)
                * Mathf.Max(0.0f, ShowdownPairAngleJitterDegrees));

        var fallback = new Transform3D(
            new Basis(Vector3.Up, yaw) * PokerCard.Orientation(false),
            new Vector3(place.X, spec.CardThickness * 0.5f, place.Y));
        if (BoardPresenter == null || !IsInstanceValid(card))
            return fallback;

        var nearWorld = ToGlobal(new Vector3(place.X, 0.0f, place.Y));
        BoardPresenter.TryGetTableSurface(nearWorld, out var surfacePoint, out var surfaceNormal);
        if (surfaceNormal.IsZeroApprox())
            surfaceNormal = Vector3.Up;
        else
            surfaceNormal = surfaceNormal.Normalized();

        // Preserve the seat-relative yaw while making the card plane tangent to the actual felt,
        // including a table model that has been translated, scaled or tilted in the editor.
        var localTurn = new Basis(Vector3.Up, yaw);
        var worldX = GlobalBasis * localTurn.X;
        worldX -= surfaceNormal * worldX.Dot(surfaceNormal);
        if (worldX.IsZeroApprox())
            worldX = surfaceNormal.Cross(Vector3.Forward);
        if (worldX.IsZeroApprox())
            worldX = Vector3.Right;
        worldX = worldX.Normalized();
        var worldZ = worldX.Cross(surfaceNormal).Normalized();
        var worldTarget = new Transform3D(
            new Basis(worldX, surfaceNormal, worldZ).Orthonormalized(), surfacePoint);

        var inversePresenter = GlobalTransform.AffineInverse();
        card.Transform = inversePresenter * worldTarget;

        // Imported card origins and thicknesses differ. Measure the visible mesh rather than
        // guessing from the root, then place its lowest corner just above the felt. The second card
        // gets its own layer so overlapping faces cannot flicker.
        if (card.TryGetVisibleProjectionRange(surfaceNormal, out var minimum, out _))
        {
            var clearance = 0.00035f
                + index * Mathf.Max(spec.CardThickness, ShowdownPairLayerSeparation);
            var requiredMinimum = surfacePoint.Dot(surfaceNormal) + clearance;
            worldTarget.Origin += surfaceNormal * (requiredMinimum - minimum);
        }

        return inversePresenter * worldTarget;
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
            : hand.OnTable > OpponentPickUpDelay
                + Mathf.Max(hand.PickUpAnimationSeconds, OpponentPickUpAnimationSeconds)
                    * Mathf.Clamp(OpponentPickUpContactFraction, 0.1f, 0.9f);

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
            if (!IsInstanceValid(card))
                continue;
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
