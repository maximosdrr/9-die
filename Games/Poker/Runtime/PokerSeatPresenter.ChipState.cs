using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;
using ChipBatch = PokerChipAnimator.Batch;
using ChipBatchPhase = PokerChipAnimator.Phase;

/// <summary>
/// Reconciles public poker state with the local physical chip presentation and exposes
/// deterministic snapshots used by recovery and presentation tests.
/// </summary>
public partial class PokerSeatPresenter : Node3D
{
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
            SnapToAuthoritativeState(spec);
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

    /// <summary>Public recovery hook for a peer that has just received a fresh authoritative snapshot.</summary>
    public void SnapToAuthoritativeState()
    {
        if (_game != null && BoardPresenter != null)
            SnapToAuthoritativeState(BoardPresenter.Spec);
    }

    private void SnapToAuthoritativeState(PokerLayoutSpec spec)
    {
        LastRecoveryDiscardedAnimation = _presentationHand >= 0
            && (_pendingChipActions.Count > 0 || _collecting || _organizing || _collectionRequested
                || HasPhase(ChipBatchPhase.ToBet, ChipBatchPhase.Landing, ChipBatchPhase.ToPot,
                    ChipBatchPhase.Organizing, ChipBatchPhase.ToDealer, ChipBatchPhase.ToWinner)
                || ((_showdownPresenter?.Active ?? false) && !(_showdownPresenter?.ReadyForPayout ?? true)));
        _showdownPresenter?.Reset();
        _presentationHand = _game.HandNumber;
        _presentationActionSeq = _game.ActionSeq;
        _visibleStreet = _game.Street;
        _requestedStreet = _game.Street;
        _collecting = false;
        _organizing = false;
        _collectionRequested = false;
        _settlementCollected = _game.HandSettled;
        _payoutSequencer?.Reset(_game.HandSettled);
        _pendingChipActions.Clear();
        _observedStacks.Clear();
        _observedCommitted.Clear();
        _displayStacks.Clear();
        _bankRuns.Clear();
        _nextChipSequence = 0;

        _chipAnimator?.ResetAll();

        BoardPresenter.EnablePresentationGate(_visibleStreet);

        foreach (var playerId in _game.SeatOrder)
        {
            _displayStacks[playerId] = _game.StackOf(playerId);
            _bankRuns[playerId] = PokerChipStack.CreatePlayableBank(_game.StackOf(playerId));
            var blind = _game.BetOf(playerId);
            if (blind > 0)
                PlaceInitialBet(playerId, blind, spec);
        }

        // PotInMiddle is already authoritative on a late join. Replaying historical calls would be
        // both impossible and visually misleading, so reconstruct the same denomination columns now.
        if (!_game.HandSettled && _game.PotInMiddle > 0)
            PlaceOrganizedPotSnapshot(_game.PotInMiddle);

        if (_game.HandSettled)
            _showdownPresenter?.Reset(authoritativeSettled: true, hand: _game.HandNumber);

        RememberPublicChipState();
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
            ChipBatchPhase.Organizing, ChipBatchPhase.ToDealer, ChipBatchPhase.ToWinner)
        && _visibleStreet >= _requestedStreet
        && (BoardPresenter?.Settled ?? true);

    /// <summary>
    /// Instance ids of the physical chip visuals currently representing bets or the pot. Primarily a
    /// regression aid: their identity must survive landing, collection and organization.
    /// </summary>
    public IReadOnlyList<ulong> ActiveChipVisualIds()
        => _chipAnimator?.ActiveVisualIds() ?? System.Array.Empty<ulong>();

    /// <summary>Current table-local positions keyed by persistent chip instance.</summary>
    public IReadOnlyDictionary<ulong, Vector3> ActiveChipVisualPositions()
        => _chipAnimator?.ActiveVisualPositions()
            ?? new Dictionary<ulong, Vector3>();

    /// <summary>Regression guard for the first rendered frame of a call or raise.</summary>
    public bool NewlyStartedBatchesAreAtTheirOrigin => _chipAnimator.Batches.All(batch =>
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

    public bool BetsAreVisuallyLoose => _chipAnimator.Batches.Any(batch =>
        batch.Phase == ChipBatchPhase.AtBet && batch.Pile.Spread > 0.95f);

    public bool PotIsLooseWhileOrganizing => _chipAnimator.Batches.Any(batch =>
        batch.Phase == ChipBatchPhase.Organizing && batch.Progress < 0.25f
        && batch.Pile.Spread > 0.70f);

    public bool PotIsOrganizedTower
    {
        get
        {
            var found = false;
            foreach (var batch in _chipAnimator.Batches)
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
    public int PotDenominationColumnCount => _chipAnimator.Batches
        .Where(batch => batch.Phase == ChipBatchPhase.InPot)
        .Select(DenominationOf).Where(value => value > 0).Distinct().Count();

    public bool PayoutStarted => _payoutSequencer?.Started ?? false;
    public bool PayoutCompleted => _payoutSequencer?.Completed ?? false;
    public bool DealerChangeInProgress => _payoutSequencer?.DealerChangeInProgress ?? false;
    public bool DealerChangeCompleted => _payoutSequencer?.DealerChangeCompleted ?? false;
    public int ChipsDeliveredToWinners => _chipAnimator.Batches.Count(batch =>
        batch.Phase == ChipBatchPhase.AtWinner);
    public int PayoutRecipientCount => _chipAnimator.Batches
        .Where(batch => batch.Phase == ChipBatchPhase.AtWinner)
        .Select(batch => batch.WinnerId).Where(id => !string.IsNullOrEmpty(id)).Distinct().Count();
    public int ActiveChipGroupCount => _chipAnimator?.ActiveBatchCount ?? 0;
    public int PayoutPhysicalValueOf(string playerId) => _chipAnimator?.Batches
        .Where(batch => batch.Phase == ChipBatchPhase.AtWinner && batch.WinnerId == playerId)
        .Sum(batch => batch.Amount) ?? 0;
    public IReadOnlyList<int> PotPhysicalGroupValues() => _chipAnimator?.Batches
        .Where(batch => batch.Phase == ChipBatchPhase.InPot)
        .OrderByDescending(batch => batch.Amount).ThenBy(batch => batch.Sequence)
        .Select(batch => batch.Amount).ToList() ?? new List<int>();

    /// <summary>
    /// Stack value currently represented on the cloth. This deliberately trails the authoritative
    /// value until the corresponding physical batch starts moving, keeping removal and take-off in
    /// the same rendered frame.
    /// </summary>
    public int DisplayedStackOf(string playerId) =>
        _displayStacks.GetValueOrDefault(playerId, _game?.StackOf(playerId) ?? 0);

    private void BuildChipBatchPool()
    {
        _chipAnimator = GetNodeOrNull<PokerChipAnimator>("ChipAnimator");
        if (_chipAnimator == null)
        {
            _chipAnimator = new PokerChipAnimator { Name = "ChipAnimator" };
            AddChild(_chipAnimator);
        }

        _chipAnimator.Configure(_game?.ChipScene, ChipScatter,
            ChipBatchPoolSize, PrewarmedChipsPerBatch,
            ChipFlightSeconds, ChipFlightArc, ChipLandingSeconds,
            ChipCollectSeconds, ChipOrganizeSeconds, ChipPayoutSeconds,
            Profile.DealerChangeSeconds);

        _chipSoundscape = GetNodeOrNull<PokerChipSoundscape>("ChipSoundscape");
        if (_chipSoundscape == null)
        {
            _chipSoundscape = new PokerChipSoundscape { Name = "ChipSoundscape" };
            AddChild(_chipSoundscape);
        }
        _chipSoundscape.Configure(_chipAnimator, ChipLandingSound,
            ChipImpactVoiceLimit, singleImpactDb: ChipSingleImpactDb,
            maximumImpactDb: ChipMaximumImpactDb);
    }

    private void BuildPresentationComponents()
    {
        _showdownPresenter = GetNodeOrNull<PokerShowdownPresenter>("ShowdownPresenter");
        if (_showdownPresenter == null)
        {
            _showdownPresenter = new PokerShowdownPresenter { Name = "ShowdownPresenter" };
            AddChild(_showdownPresenter);
        }
        _showdownPresenter.Configure(_game, BoardPresenter, CardScene, Profile,
            ShowdownSourceTransform, NameOf, HideShowdownSources,
            ShowdownRowSpacing, ShowdownCardSpacing, ShowdownArc,
            ShowdownWinnerColor, ShowdownOtherColor);

        _payoutSequencer = GetNodeOrNull<PokerPayoutSequencer>("PayoutSequencer");
        if (_payoutSequencer == null)
        {
            _payoutSequencer = new PokerPayoutSequencer { Name = "PayoutSequencer" };
            AddChild(_payoutSequencer);
        }
        _payoutSequencer.Configure(_chipAnimator, Profile, MaxAnimatedChipGroups,
            AcquireBatch, NextChipSequence, TryPayoutSeatPlaces, WinnerStackOffset);
        SignalUtil.ConnectGuarded(_payoutSequencer,
            PokerPayoutSequencer.SignalName.DealerChangeStarted,
            new Callable(this, MethodName.OnDealerChangeStarted));
    }

    private void OnDealerChangeStarted()
    {
        if (DealerAnimator != null && !string.IsNullOrWhiteSpace(DealerChangeAnimation)
            && DealerAnimator.HasAnimation(DealerChangeAnimation))
            DealerAnimator.Play(DealerChangeAnimation);

        if (DealerChangeSound == null)
            return;
        _dealerChangeAudio ??= new AudioStreamPlayer3D
        {
            Name = "DealerChangeAudio",
            UnitSize = 2.0f,
            MaxDistance = 10.0f,
        };
        if (_dealerChangeAudio.GetParent() == null)
            AddChild(_dealerChangeAudio);
        _dealerChangeAudio.Position = BoardPresenter.DeckPosition;
        _dealerChangeAudio.Stream = DealerChangeSound;
        _dealerChangeAudio.Play();
    }

    private int NextChipSequence() => _nextChipSequence++;

    private bool TryPayoutSeatPlaces(string playerId, out Basis basis, out Vector3 stack)
        => TrySeatChipPlaces(playerId, BoardPresenter.Spec, out basis, out stack, out _);

}
