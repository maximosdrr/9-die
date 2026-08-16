using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;
using ChipBatch = PokerChipAnimator.Batch;
using ChipBatchPhase = PokerChipAnimator.Phase;

/// <summary>
/// Runs chip payment, collection, organization and payout motion without changing game rules.
/// </summary>
public partial class PokerSeatPresenter : Node3D
{
    private ChipBatch AcquireBatch() => _chipAnimator.Acquire();

    private void PlaceInitialBet(
        string playerId, IReadOnlyList<ChipRun> authoritativeRuns, int amount, PokerLayoutSpec spec)
    {
        if (!TrySeatChipPlaces(playerId, spec, out var basis, out _, out var bet))
            return;

        var available = Mathf.Max(1, MaxAnimatedChipGroups - _chipAnimator.ActiveBatchCount);
        var runs = authoritativeRuns is { Count: > 0 }
            ? authoritativeRuns : PokerChipStack.Decompose(amount);
        foreach (var run in PokerChipAnimator.GroupRuns(runs, available))
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

    private void PlaceOrganizedPotSnapshot(IReadOnlyList<ChipRun> authoritativeRuns, int amount)
    {
        var centre = BoardPresenter.PotPosition;
        var available = Mathf.Max(1, MaxAnimatedChipGroups - _chipAnimator.ActiveBatchCount);
        var runs = authoritativeRuns is { Count: > 0 }
            ? authoritativeRuns : PokerChipStack.Decompose(amount);
        foreach (var run in PokerChipAnimator.GroupRuns(runs, available))
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

        BeginOrganization(playSound: false);
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

        // The blind and any earlier contribution by this player are already physical AtBet actors.
        // Confirming a wager pushes the whole contribution as one gesture; moving only the freshly
        // selected actors made the blind appear glued to its old position.
        BeginCommittedWagerPush(action.PlayerId, basis, bet);

        var authoritativePayment = action.Runs is { Count: > 0 }
            ? action.Runs.Select(run => new ChipRun(run.Denomination, run.Count)).ToList()
            : null;
        var prepared = TakeSubmittedPreparedWager(action.PlayerId, action.Amount);
        var preparedWasLocal = prepared.Count > 0;
        if (!preparedWasLocal)
            prepared = TakeReplicatedPreparedWager(
                action.PlayerId, action.ActionSeq, action.Amount, authoritativePayment);
        var usesPreparedChips = prepared.Count > 0;
        var preparedPayment = prepared.Select(chip => chip.Run).ToList();
        if (usesPreparedChips && authoritativePayment is { Count: > 0 }
            && !SameChipComposition(preparedPayment, authoritativePayment))
        {
            // Recovery/race safety: discard the tentative rendering and trust the server's ledger.
            ReleaseConsumedPreparedWager(prepared);
            prepared.Clear();
            usesPreparedChips = false;
        }

        // Once the server confirms the exact same composition, keep the one-chip runs in the order
        // selected by the player. Regrouping them here was the now-redundant "disorganize" animation.
        var payment = usesPreparedChips
            ? preparedPayment
            : authoritativePayment is { Count: > 0 }
                ? authoritativePayment
                : TakeVisualPayment(action.PlayerId, action.Amount, action.StackAfter);

        var publicBank = _game.ChipBankOf(action.PlayerId);
        if (publicBank.Count > 0)
            _bankRuns[action.PlayerId] = publicBank
                .Select(run => new ChipRun(run.Denomination, run.Count)).ToList();
        if (_stacks.TryGetValue(action.PlayerId, out var bankPile)
            && _bankRuns.TryGetValue(action.PlayerId, out var bank))
            bankPile.SetRuns(bank);

        var groupIndex = 0;
        var paidByDenomination = new Dictionary<int, int>();
        var available = Mathf.Max(1, MaxAnimatedChipGroups - _chipAnimator.ActiveBatchCount);
        var movingRuns = usesPreparedChips
            ? payment
            : PokerChipAnimator.GroupRuns(payment, available);
        foreach (var run in movingRuns)
        {
            var movingIndex = groupIndex++;
            var batch = AcquireBatch();
            batch.PlayerId = action.PlayerId;
            batch.Amount = run.Value;
            batch.Basis = basis;
            batch.Sequence = _nextChipSequence++;
            batch.To = bet + basis * Vector3.Forward * CommittedWagerPushDistance;
            batch.Progress = 0.0f;
            var paidBefore = paidByDenomination.GetValueOrDefault(run.Denomination);
            paidByDenomination[run.Denomination] = paidBefore + run.Count;
            if (usesPreparedChips)
            {
                // The selection animation already placed this exact chip on the cloth. Adopt it and
                // slide it a few centimetres toward the centre, synchronized with the hand/body push.
                var transfer = prepared[movingIndex];
                _chipAnimator.Adopt(batch, transfer.Pile);
                batch.Basis = batch.Pile.Basis;
                batch.From = batch.Pile.Position;
                // Preserve the canonical slot chosen while the chip was tentative. Recounting after
                // setting AtBet includes this very batch and shifts the loose destination by one.
                batch.Pile.LooseSlotOffset = transfer.BetSlot;
                batch.To = CommittedBetTarget(batch.Pile, basis, bet);
                batch.FromBasis = batch.Pile.Basis;
                batch.ToBasis = batch.Pile.Basis;
                batch.Duration = CommittedWagerPushSeconds;
                batch.Delay = 0.0f;
                batch.Phase = ChipBatchPhase.PushingBet;
                batch.Pile.Spread = 0.0f;
                batch.Pile.FlightProgress = 1.0f;
                batch.JustStarted = false;
                batch.Pile.Visible = true;
                continue;
            }

            batch.Delay = movingIndex * Profile.ChipFlightStagger;
            batch.Phase = ChipBatchPhase.ToBet;
            var departure = _bankRuns.TryGetValue(action.PlayerId, out var bankAfter)
                ? PaymentDepartureOffset(bankAfter, run.Denomination, paidBefore, bankPile)
                : Vector3.Up * (bankPile?.TopHeight ?? 0.0f);
            var bankBasis = bankPile?.Basis ?? basis;
            batch.From = stack + bankBasis * departure;

            // Configure while hidden and reveal at the real physical source. A large stack may travel
            // as a compact same-denomination group, but it never changes value or chip type in flight.
            batch.Pile.Visible = false;
            batch.Pile.Transform = new Transform3D(bankBasis, batch.From);
            batch.Pile.LooseSlotOffset = NextBetLooseSlot(action.PlayerId);
            batch.Pile.Spread = 0.0f;
            batch.Pile.FlightProgress = 0.0f;
            batch.Pile.SetRuns(new[] { run });
            batch.JustStarted = true;
            batch.Pile.Visible = true;
        }
        if (usesPreparedChips && preparedWasLocal)
            CompletePreparedWagerAdoption();
        return groupIndex > 0;
    }

    private void BeginCommittedWagerPush(string playerId, Basis basis, Vector3 bet)
    {
        foreach (var batch in _chipAnimator.Batches)
        {
            if (batch.PlayerId != playerId || batch.Phase != ChipBatchPhase.AtBet)
                continue;

            var target = CommittedBetTarget(batch.Pile, basis, bet);
            if (batch.Pile.Position.DistanceTo(target) < 0.0001f)
                continue;

            batch.From = batch.Pile.Position;
            batch.To = target;
            batch.FromBasis = batch.Pile.Basis;
            batch.ToBasis = batch.Pile.Basis;
            batch.Progress = 0.0f;
            batch.Delay = 0.0f;
            batch.Duration = CommittedWagerPushSeconds;
            batch.JustStarted = false;
            batch.Phase = ChipBatchPhase.PushingBet;
            batch.Pile.FlightProgress = 1.0f;
        }
    }

    private Vector3 CommittedBetTarget(PokerChipPile pile, Basis basis, Vector3 bet)
    {
        var target = bet;
        if (pile != null && pile.Spread < 0.5f)
        {
            target += basis * PokerChipContactLayout.RootOffset(
                pile.LooseSlotOffset, pile.EffectiveDiameter, pile.EffectiveThickness);
        }

        return target + basis * Vector3.Forward * CommittedWagerPushDistance;
    }

    private static bool SameChipComposition(
        IReadOnlyList<ChipRun> left, IReadOnlyList<ChipRun> right)
    {
        if (PokerChipStack.Total(left) != PokerChipStack.Total(right)
            || PokerChipStack.ChipCount(left) != PokerChipStack.ChipCount(right))
            return false;
        var leftCounts = left.GroupBy(run => run.Denomination)
            .ToDictionary(group => group.Key, group => group.Sum(run => run.Count));
        var rightCounts = right.GroupBy(run => run.Denomination)
            .ToDictionary(group => group.Key, group => group.Sum(run => run.Count));
        return leftCounts.Count == rightCounts.Count
            && leftCounts.All(entry => rightCounts.GetValueOrDefault(entry.Key) == entry.Value);
    }

    private int NextBetLooseSlot(string playerId)
    {
        var next = 0;
        foreach (var batch in _chipAnimator.Batches)
        {
            if (batch.PlayerId != playerId || batch.Phase is not
                (ChipBatchPhase.ToBet or ChipBatchPhase.PushingBet
                    or ChipBatchPhase.Landing or ChipBatchPhase.AtBet))
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
        var across = new Vector2(-facing.Y, facing.X);
        var bet2 = PokerTableLayout.SeatSpot(facing, spec.SeatBetRadius)
            - across * BetSideOffset;
        stack = TryAuthoredStackTransform(playerId, out var authoredStack)
            ? authoredStack.Origin
            : DefaultStackTransform(facing, spec).Origin;
        bet = new Vector3(bet2.X, 0.0f, bet2.Y);
        return true;
    }

    private bool AdvanceChipPresentation(float delta)
    {
        var moved = false;

        // An action from the visible street goes first, even when it is the call that requested the
        // following street. Actions already received for a future street wait behind collection.
        if (!_collecting && !HasPhase(
                ChipBatchPhase.ToBet, ChipBatchPhase.PushingBet, ChipBatchPhase.Landing)
            && _pendingChipActions.TryPeek(out var next) && next.Street <= _visibleStreet)
        {
            _pendingChipActions.Dequeue();
            moved |= StartChipAction(next);
        }

        foreach (var batch in _chipAnimator.Batches)
        {
            var phaseBefore = batch.Phase;
            moved |= _chipAnimator.Advance(batch, delta);
            if (phaseBefore == ChipBatchPhase.OrganizingWinner
                && batch.Phase == ChipBatchPhase.AtWinner)
            {
                _displayStacks[batch.WinnerId] = Mathf.Min(_game.StackOf(batch.WinnerId),
                    _displayStacks.GetValueOrDefault(batch.WinnerId) + batch.Amount);
            }
        }

        var payoutWasCompleted = _payoutSequencer?.Completed ?? false;
        moved |= _payoutSequencer?.Advance(delta) ?? false;

        var visibleActionPending = _pendingChipActions.TryPeek(out var queued)
            && queued.Street <= _visibleStreet;
        if (_collectionRequested && !_collecting && !visibleActionPending
            && _actionGestureRemaining <= 0.0f
            && !HasPhase(ChipBatchPhase.ToBet, ChipBatchPhase.PushingBet,
                ChipBatchPhase.Landing))
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
                BoardPositionToPresenter(BoardPresenter.DeckPosition)
                + BoardBasisToPresenter(Vector3.Up) * 0.006f) ?? false;
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
            batch.StartSpread = batch.Pile.Spread;
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

    private void BeginOrganization(bool playSound = true)
    {
        _organizing = true;
        var reader = new Basis(Vector3.Up, ReaderYaw(Vector2.Down));
        if (!_chipAnimator.BeginOrganization(BoardPresenter.PotPosition, reader, PotColumnSpacing))
        {
            _organizing = false;
            return;
        }

        if (playSound)
        {
            var chipCount = _chipAnimator.Batches
                .Where(batch => batch.Phase == ChipBatchPhase.Organizing)
                .Sum(batch => batch.Pile.ChipCount);
            _chipSoundscape?.PlayOrganization(
                ToGlobal(BoardPresenter.PotPosition), chipCount, ChipOrganizeSeconds);
        }
    }

    private static int DenominationOf(ChipBatch batch) => PokerChipAnimator.DenominationOf(batch);

    private bool ShouldBeginPayout()
    {
        if ((_payoutSequencer?.Started ?? false) || (_payoutSequencer?.Completed ?? false)
            || !_game.HandSettled || !_settlementCollected
            || _collecting || _organizing || _collectionRequested
            || HasPhase(ChipBatchPhase.ToBet, ChipBatchPhase.PushingBet,
                ChipBatchPhase.Landing,
                ChipBatchPhase.ToPot, ChipBatchPhase.Organizing, ChipBatchPhase.ToDealer,
                ChipBatchPhase.ToWinner, ChipBatchPhase.AtWinnerLoose,
                ChipBatchPhase.OrganizingWinner))
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
}
