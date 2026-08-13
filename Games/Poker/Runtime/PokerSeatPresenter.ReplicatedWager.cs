using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

/// <summary>
/// Mirrors another player's reversible chip selection. The authority publishes only an ordered
/// denomination snapshot; this presenter keeps the physical actors stable between revisions and
/// hands those exact actors to the normal bet animator after the action is accepted.
/// </summary>
public partial class PokerSeatPresenter : Node3D
{
    private sealed class ReplicatedPreparedChip
    {
        public PokerChipPile Pile;
        public int Denomination;
        public int Lane;
        public int BetSlot;
        public Vector3 From;
        public Vector3 To;
        public Basis FromBasis = Basis.Identity;
        public Basis ToBasis = Basis.Identity;
        public int MotionSeed;
        public float Progress;
        public bool Returning;
        public bool ImpactPlayed;
    }

    private sealed class ReplicatedPreparedWager
    {
        public string PlayerId = "";
        public int TurnToken;
        public int Revision = -1;
        public int CommittedActionSeq = -1;
        public int BaseBetSlot = -1;
        public readonly List<ReplicatedPreparedChip> Chips = new();

        public int Amount => Chips.Where(chip => !chip.Returning)
            .Sum(chip => chip.Denomination);
    }

    private readonly Dictionary<string, ReplicatedPreparedWager> _replicatedPrepared = new();
    /// <summary>
    /// A committed snapshot may remain current for another presentation frame after its actors were
    /// adopted. Keep a short-lived tombstone so Sync cannot recreate and debit the same chips twice.
    /// </summary>
    private readonly HashSet<string> _consumedReplicatedActions = new();

    private static string ReplicatedWagerKey(string playerId, int turnToken) =>
        $"{playerId}:{turnToken}";

    private static string ReplicatedActionKey(string playerId, int actionSeq) =>
        $"{playerId}:{actionSeq}";

    /// <summary>Reconciles the latest server-approved preview without replacing surviving actors.</summary>
    private void SyncReplicatedPreparedWagers()
    {
        if (_game == null || BoardPresenter == null)
            return;

        var localId = _game.Player == null ? "" : (string)_game.Player.Name;
        var published = new HashSet<string>();
        var publishedCommittedActions = new HashSet<string>();
        foreach (var entry in _game.PreparedWagers)
        {
            var snapshot = entry.Value;
            if (snapshot == null || snapshot.PlayerId == localId)
                continue;

            var key = ReplicatedWagerKey(snapshot.PlayerId, snapshot.TurnToken);
            published.Add(key);
            if (snapshot.IsCommitted)
            {
                var actionKey = ReplicatedActionKey(
                    snapshot.PlayerId, snapshot.CommittedActionSeq);
                publishedCommittedActions.Add(actionKey);
                if (_consumedReplicatedActions.Contains(actionKey))
                {
                    if (_replicatedPrepared.TryGetValue(key, out var duplicate))
                        DiscardReplicatedPreparedWager(duplicate);
                    continue;
                }
            }

            if (!_replicatedPrepared.TryGetValue(key, out var wager))
            {
                wager = new ReplicatedPreparedWager
                {
                    PlayerId = snapshot.PlayerId,
                    TurnToken = snapshot.TurnToken,
                    BaseBetSlot = ReplicatedBaseBetSlot(snapshot),
                };
                _replicatedPrepared[key] = wager;
            }

            if (snapshot.Revision < wager.Revision)
                continue;

            wager.Revision = snapshot.Revision;
            wager.CommittedActionSeq = snapshot.CommittedActionSeq;
            ReconcileReplicatedChips(wager, snapshot.Denominations);
        }

        foreach (var entry in _replicatedPrepared.ToArray())
        {
            var wager = entry.Value;
            // A committed preview may disappear from the next-turn snapshot before its queued table
            // animation starts. Keep it until StartChipAction adopts the actors by ActionSeq.
            if (published.Contains(entry.Key) || wager.CommittedActionSeq >= 0)
                continue;
            BeginReplicatedReturn(wager);
        }

        _consumedReplicatedActions.RemoveWhere(
            actionKey => !publishedCommittedActions.Contains(actionKey));
    }

    private int ReplicatedBaseBetSlot(PokerPreparedWagerSnapshot snapshot)
    {
        if (_game == null || snapshot == null)
            return 0;

        var committed = PokerChipStack.ChipCount(_game.RoundChipsOf(snapshot.PlayerId));
        // In the accepted-action context RoundChips already includes this preview. Peers that saw
        // only that context must still derive the same base as peers that saw the reversible preview.
        return Mathf.Max(0, committed - (snapshot.IsCommitted ? snapshot.Denominations.Count : 0));
    }

    private void ReconcileReplicatedChips(
        ReplicatedPreparedWager wager, IReadOnlyList<int> denominations)
    {
        denominations ??= System.Array.Empty<int>();
        var available = wager.Chips.ToList();
        var retained = new HashSet<ReplicatedPreparedChip>();
        var ordered = new List<ReplicatedPreparedChip>(denominations.Count);

        for (var index = 0; index < denominations.Count; index++)
        {
            var denomination = denominations[index];
            var betSlot = Mathf.Max(0, wager.BaseBetSlot) + index;
            // Prefer an actor that never started returning. If the same denomination is selected
            // again before its return lands, reverse that very actor instead of debiting/creating a
            // second chip while the first is still physically visible.
            var existing = available.FirstOrDefault(chip => !retained.Contains(chip)
                && !chip.Returning && chip.Denomination == denomination)
                ?? available.FirstOrDefault(chip => !retained.Contains(chip)
                    && chip.Returning && chip.Denomination == denomination);
            if (existing != null)
            {
                retained.Add(existing);
                ordered.Add(existing);
                RetargetReplicatedChip(wager.PlayerId, existing, betSlot);
                continue;
            }

            var created = CreateReplicatedPreparedChip(
                wager.PlayerId, denomination, betSlot);
            if (created == null)
                continue;
            wager.Chips.Add(created);
            retained.Add(created);
            ordered.Add(created);
        }

        foreach (var chip in available)
        {
            if (!retained.Contains(chip))
                BeginReplicatedReturn(wager, chip);
        }

        // The snapshot order is presentation identity. Keep transfers in that order and append only
        // actors that are on their way home, which are excluded from Amount and later adoption.
        var returning = wager.Chips.Where(chip => !retained.Contains(chip)).ToList();
        wager.Chips.Clear();
        wager.Chips.AddRange(ordered);
        wager.Chips.AddRange(returning);
    }

    private ReplicatedPreparedChip CreateReplicatedPreparedChip(
        string playerId, int denomination, int betSlot)
    {
        if (!_stacks.TryGetValue(playerId, out var bankPile))
            return null;

        if (!_bankRuns.TryGetValue(playerId, out var bank))
        {
            bank = _game.ChipBankOf(playerId)
                .Select(run => new ChipRun(run.Denomination, run.Count)).ToList();
            _bankRuns[playerId] = bank;
        }

        var lane = bank.FindIndex(run => run.Denomination == denomination && run.Count > 0);
        if (lane < 0 || !TrySeatChipPlaces(playerId, BoardPresenter.Spec,
                out var basis, out var stack, out var bet))
            return null;

        var run = bank[lane];
        bank[lane] = new ChipRun(run.Denomination, run.Count - 1);
        bankPile.SetRuns(bank);

        var x = LaneOffset(lane, bank.Count, bankPile.StackSpacing);
        var source = stack + bankPile.Basis * new Vector3(
            x, bank[lane].Count * bankPile.EffectiveThickness, 0.0f);
        var target = PreparedBetPlace(playerId, bet) + basis * PokerChipContactLayout.RootOffset(
            betSlot, bankPile.EffectiveDiameter, bankPile.EffectiveThickness);
        var motionSeed = denomination * 17 + lane * 31 + betSlot * 53;
        var restBasis = basis
                        * new Basis(Vector3.Up,
                            PokerChipPile.Noise(motionSeed, 61) * 0.42f);

        var pile = NewReplicatedPreparedPile(denomination, bankPile.Basis, source, betSlot);
        return new ReplicatedPreparedChip
        {
            Pile = pile,
            Denomination = denomination,
            Lane = lane,
            BetSlot = betSlot,
            From = source,
            To = target,
            FromBasis = bankPile.Basis,
            ToBasis = restBasis,
            MotionSeed = motionSeed,
        };
    }

    private void RetargetReplicatedChip(
        string playerId, ReplicatedPreparedChip chip, int betSlot)
    {
        if (chip?.Pile == null || !_stacks.TryGetValue(playerId, out var bankPile)
            || !TrySeatChipPlaces(playerId, BoardPresenter.Spec,
                out var basis, out _, out var bet))
            return;

        var target = PreparedBetPlace(playerId, bet) + basis * PokerChipContactLayout.RootOffset(
            betSlot, bankPile.EffectiveDiameter, bankPile.EffectiveThickness);
        var needsMotion = chip.Returning || chip.BetSlot != betSlot
                          || chip.To.DistanceTo(target) > 0.0001f;
        chip.Returning = false;
        chip.BetSlot = betSlot;
        if (!needsMotion)
            return;

        chip.From = chip.Pile.Position;
        chip.To = target;
        chip.FromBasis = chip.Pile.Basis;
        chip.MotionSeed = chip.Denomination * 17 + chip.Lane * 31 + betSlot * 53;
        chip.ToBasis = basis * new Basis(Vector3.Up,
            PokerChipPile.Noise(chip.MotionSeed, 61) * 0.42f);
        chip.Progress = 0.0f;
        chip.ImpactPlayed = false;
        chip.Pile.FlightProgress = 0.0f;
    }

    private PokerChipPile NewReplicatedPreparedPile(
        int denomination, Basis basis, Vector3 source, int betSlot)
    {
        var pile = new PokerChipPile
        {
            Name = $"ReplicatedPreparedChip{betSlot}",
            ChipScene = _game?.ChipScene,
            Scatter = ChipScatter,
            CombineRunsIntoColumns = true,
            LooseWhenSpread = true,
            Spread = 0.0f,
            SettleSeconds = 0.0f,
        };
        AddChild(pile);
        pile.Visible = false;
        pile.Transform = new Transform3D(basis, source);
        pile.SetRuns(new[] { new ChipRun(denomination, 1) });
        pile.FlightProgress = 0.0f;
        pile.Visible = true;
        return pile;
    }

    private void BeginReplicatedReturn(ReplicatedPreparedWager wager)
    {
        foreach (var chip in wager.Chips.ToArray())
            BeginReplicatedReturn(wager, chip);
    }

    private void BeginReplicatedReturn(
        ReplicatedPreparedWager wager, ReplicatedPreparedChip chip)
    {
        if (chip == null || chip.Returning)
            return;

        chip.Returning = true;
        chip.Progress = 0.0f;
        chip.From = chip.Pile.Position;
        chip.To = ReplicatedReturnTarget(wager.PlayerId, chip.Lane, chip.Denomination);
        chip.FromBasis = chip.Pile.Basis;
        chip.ToBasis = _stacks.TryGetValue(wager.PlayerId, out var bankPile)
            ? bankPile.Basis : Basis.Identity;
        chip.Pile.FlightProgress = 0.0f;
    }

    private Vector3 ReplicatedReturnTarget(string playerId, int lane, int denomination)
    {
        if (!_stacks.TryGetValue(playerId, out var bankPile)
            || !_bankRuns.TryGetValue(playerId, out var bank)
            || lane < 0 || lane >= bank.Count
            || !TrySeatChipPlaces(playerId, BoardPresenter.Spec,
                out var basis, out var stack, out _))
            return Vector3.Zero;

        var returningBelow = _replicatedPrepared.Values
            .Where(wager => wager.PlayerId == playerId)
            .SelectMany(wager => wager.Chips)
            .Count(chip => chip.Returning && chip.Denomination == denomination);
        var height = bank[lane].Count + Mathf.Max(0, returningBelow - 1);
        var x = LaneOffset(lane, bank.Count, bankPile.StackSpacing);
        return stack + bankPile.Basis * new Vector3(
            x, height * bankPile.EffectiveThickness, 0.0f);
    }

    private bool AdvanceReplicatedPreparedWagers(float delta)
    {
        var moved = false;
        foreach (var entry in _replicatedPrepared.ToArray())
        {
            var wager = entry.Value;
            for (var index = wager.Chips.Count - 1; index >= 0; index--)
            {
                var chip = wager.Chips[index];
                if (chip.Progress >= 1.0f)
                    continue;

                var before = chip.Progress;
                chip.Progress = Mathf.Min(1.0f, chip.Progress
                    + Mathf.Max(0.0f, delta) / Mathf.Max(PreparedChipMoveSeconds, 0.01f));
                chip.Pile.Position = PokerMotion.ChipThrow(
                    chip.From, chip.To, chip.Progress, PreparedChipArc,
                    PokerChipPile.Noise(chip.Lane + chip.Denomination, 47) * 0.005f);
                var eased = PokerMotion.Smooth(chip.Progress);
                var basis = new Transform3D(chip.FromBasis, Vector3.Zero).InterpolateWith(
                    new Transform3D(chip.ToBasis, Vector3.Zero), eased).Basis;
                if (chip.Progress < 1.0f)
                {
                    var tumble = Mathf.Sin(chip.Progress * Mathf.Pi);
                    basis *= new Basis(Vector3.Right,
                                 PokerChipPile.Noise(chip.MotionSeed, 64) * 0.20f * tumble)
                             * new Basis(Vector3.Forward,
                                 PokerChipPile.Noise(chip.MotionSeed, 65) * 0.14f * tumble);
                }
                chip.Pile.Basis = basis;
                chip.Pile.FlightProgress = chip.Progress;
                moved = true;

                if (!chip.Returning && !chip.ImpactPlayed && before < 1.0f
                    && chip.Progress >= 1.0f)
                {
                    chip.ImpactPlayed = true;
                    _chipSoundscape?.PlayImpact(chip.Pile.GlobalPosition, 1);
                }

                if (!chip.Returning || chip.Progress < 1.0f)
                    continue;

                AddReplicatedChipBackToBank(wager.PlayerId, chip.Denomination);
                chip.Pile.Visible = false;
                chip.Pile.QueueFree();
                wager.Chips.RemoveAt(index);
            }

            if (wager.Chips.Count == 0 && wager.CommittedActionSeq < 0)
                _replicatedPrepared.Remove(entry.Key);
        }
        return moved;
    }

    private void AddReplicatedChipBackToBank(string playerId, int denomination)
    {
        if (!_bankRuns.TryGetValue(playerId, out var bank))
            return;
        AddOne(bank, denomination);
        if (_stacks.TryGetValue(playerId, out var pile))
            pile.SetRuns(bank);
    }

    /// <summary>Transfers a committed remote preview into the ordinary persistent chip animator.</summary>
    private List<PreparedTransfer> TakeReplicatedPreparedWager(
        string playerId, int actionSeq, int amount,
        IReadOnlyList<ChipRun> authoritativePayment)
    {
        var state = _replicatedPrepared.Values
            .Where(wager => wager.PlayerId == playerId
                            && wager.CommittedActionSeq == actionSeq)
            .FirstOrDefault();
        if (state == null)
        {
            DiscardCommittedReplicatedWagersThrough(playerId, actionSeq);
            return new List<PreparedTransfer>();
        }

        if (state.Amount != amount || state.Chips.Any(chip => chip.Returning))
        {
            ConsumeAndDiscardReplicatedWager(state);
            DiscardCommittedReplicatedWagersThrough(playerId, actionSeq);
            return new List<PreparedTransfer>();
        }

        var transfers = state.Chips
            .Select(chip => new PreparedTransfer(
                chip.Denomination, chip.Pile, chip.BetSlot)).ToList();
        if (authoritativePayment is { Count: > 0 }
            && !SameChipComposition(
                transfers.Select(transfer => transfer.Run).ToList(), authoritativePayment))
        {
            ConsumeAndDiscardReplicatedWager(state);
            DiscardCommittedReplicatedWagersThrough(playerId, actionSeq);
            return new List<PreparedTransfer>();
        }

        _consumedReplicatedActions.Add(
            ReplicatedActionKey(state.PlayerId, state.CommittedActionSeq));
        _replicatedPrepared.Remove(
            ReplicatedWagerKey(state.PlayerId, state.TurnToken));
        DiscardCommittedReplicatedWagersThrough(playerId, actionSeq - 1);
        return transfers;
    }

    private void ConsumeAndDiscardReplicatedWager(ReplicatedPreparedWager wager)
    {
        if (wager == null)
            return;
        if (wager.CommittedActionSeq >= 0)
            _consumedReplicatedActions.Add(
                ReplicatedActionKey(wager.PlayerId, wager.CommittedActionSeq));
        DiscardReplicatedPreparedWager(wager);
    }

    private void DiscardCommittedReplicatedWagersThrough(string playerId, int actionSeq)
    {
        foreach (var stale in _replicatedPrepared.Values.Where(wager =>
                     wager.PlayerId == playerId && wager.CommittedActionSeq >= 0
                     && wager.CommittedActionSeq <= actionSeq).ToArray())
            ConsumeAndDiscardReplicatedWager(stale);
    }

    private void DiscardReplicatedPreparedWager(ReplicatedPreparedWager wager)
    {
        if (wager == null)
            return;
        foreach (var chip in wager.Chips)
        {
            chip.Pile.Visible = false;
            chip.Pile.QueueFree();
        }
        wager.Chips.Clear();
        _replicatedPrepared.Remove(ReplicatedWagerKey(wager.PlayerId, wager.TurnToken));
    }

    private void ClearReplicatedPreparedWagers()
    {
        foreach (var wager in _replicatedPrepared.Values.ToArray())
            DiscardReplicatedPreparedWager(wager);
        _replicatedPrepared.Clear();
        _consumedReplicatedActions.Clear();
        // Recovery already snapped directly to this authoritative action. If its short-lived
        // committed preview is still in the context, treating it as new would debit the bank twice.
        if (_game == null)
            return;
        foreach (var snapshot in _game.PreparedWagers.Values)
        {
            if (snapshot?.IsCommitted == true)
                _consumedReplicatedActions.Add(
                    ReplicatedActionKey(snapshot.PlayerId, snapshot.CommittedActionSeq));
        }
    }

    public int ReplicatedPreparedWagerChipCount(string playerId) =>
        _replicatedPrepared.Values.Where(wager => wager.PlayerId == playerId)
            .SelectMany(wager => wager.Chips).Count(chip => !chip.Returning);

    public IReadOnlyList<ulong> ReplicatedPreparedWagerVisualIds(string playerId) =>
        _replicatedPrepared.Values.Where(wager => wager.PlayerId == playerId)
            .SelectMany(wager => wager.Chips).Where(chip => !chip.Returning)
            .SelectMany(chip => chip.Pile.GetChildren().OfType<Node3D>())
            .Where(visual => visual.Visible).Select(visual => visual.GetInstanceId()).ToList();
}
