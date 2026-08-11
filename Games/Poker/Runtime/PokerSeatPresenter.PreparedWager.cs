using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

/// <summary>
/// Local, reversible chip preparation. The selecting peer keeps these exact actors responsive while
/// the authority mirrors their ordered denominations; other peers reconstruct persistent preview
/// actors in PokerSeatPresenter.ReplicatedWager and later adopt them into the confirmed bet.
/// </summary>
public partial class PokerSeatPresenter : Node3D
{
    [ExportGroup("Local wager preparation")]
    /// <summary>
    /// A selected chip advances toward the centre while staying aligned with its denomination lane.
    /// This reads as "taken out of the stack" without spending a second lateral area on the table.
    /// </summary>
    [Export] public float PreparedWagerForwardInset = 0.065f;
    [Export] public float PreparedChipMoveSeconds = 0.34f;
    [Export] public float PreparedChipArc = 0.028f;
    [Export] public float PreparedChipPickRadius = 0.030f;

    private sealed class PreparedChip
    {
        public PokerChipPile Pile;
        public int Denomination;
        public int Lane;
        public Vector3 From;
        public Vector3 To;
        public Basis FromBasis = Basis.Identity;
        public Basis ToBasis = Basis.Identity;
        public int MotionSeed;
        public int BetSlot;
        public float Progress;
        public bool Included = true;
        public bool Returning;
        public bool ImpactPlayed;
    }

    private readonly struct PreparedTransfer
    {
        public readonly ChipRun Run;
        public readonly PokerChipPile Pile;
        public readonly int BetSlot;

        public PreparedTransfer(int denomination, PokerChipPile pile, int betSlot)
        {
            Run = new ChipRun(denomination, 1);
            Pile = pile;
            BetSlot = betSlot;
        }
    }

    private readonly List<PreparedChip> _preparedChips = new();
    private string _preparedPlayerId = "";
    private bool _preparedSubmitted;

    public int PreparedWagerAmount => _preparedChips
        .Where(chip => chip.Included).Sum(chip => chip.Denomination);

    public int PreparedWagerChipCount => _preparedChips.Count(chip => chip.Included);
    public bool PreparedWagerSubmitted => _preparedSubmitted;
    public int[] PreparedWagerDenominations => _preparedChips
        .Where(chip => chip.Included).Select(chip => chip.Denomination).ToArray();
    public IReadOnlyList<Vector2> PreparedWagerVisualPositions() => _preparedChips
        .Where(chip => chip.Included)
        .Select(chip => new Vector2(chip.Pile.Position.X, chip.Pile.Position.Z))
        .ToList();

    public IReadOnlyList<ulong> PreparedWagerVisualIds() => _preparedChips
        .Where(chip => chip.Included)
        .SelectMany(chip => chip.Pile.GetChildren().OfType<Node3D>())
        .Where(visual => visual.Visible)
        .Select(visual => visual.GetInstanceId())
        .ToList();

    public IReadOnlyDictionary<ulong, Vector3> PreparedWagerVisuals()
    {
        var visuals = new Dictionary<ulong, Vector3>();
        foreach (var chip in _preparedChips.Where(chip => chip.Included))
        foreach (var child in chip.Pile.GetChildren())
        {
            if (child is Node3D { Visible: true } visual)
                visuals[visual.GetInstanceId()] = chip.Pile.Transform * visual.Position;
        }
        return visuals;
    }

    public bool TryGetBankLaneAimPoint(
        string playerId, int denomination, out Vector2 boardAim)
    {
        boardAim = Vector2.Zero;
        if (!IsLocalWagerPlayer(playerId) || BoardPresenter == null
            || !_stacks.TryGetValue(playerId, out var pile)
            || !_bankRuns.TryGetValue(playerId, out var bank))
            return false;

        var lane = bank.FindIndex(run => run.Denomination == denomination && run.Count > 0);
        if (lane < 0 || !TrySeatChipPlaces(playerId, BoardPresenter.Spec,
                out var basis, out var stack, out _))
            return false;

        var point = stack + pile.Basis * new Vector3(
            LaneOffset(lane, bank.Count, pile.StackSpacing), 0.0f, 0.0f);
        var world = ToGlobal(point);
        var local = BoardPresenter.ToLocal(world);
        boardAim = new Vector2(local.X, local.Z);
        return true;
    }

    /// <summary>Selects the top chip in the denomination lane under the table aim.</summary>
    public bool TrySelectPreparedChip(
        string playerId, Vector2 boardAim, out int denomination)
    {
        denomination = 0;
        if (_preparedSubmitted || !IsLocalWagerPlayer(playerId)
            || BoardPresenter == null || !_stacks.TryGetValue(playerId, out var bankPile)
            || !_bankRuns.TryGetValue(playerId, out var bank) || bank.Count == 0)
            return false;

        var aim = BoardAimInPresenter(boardAim);
        var local = bankPile.Transform.AffineInverse()
            * new Vector3(aim.X, 0.0f, aim.Y);
        var radius = Mathf.Max(PreparedChipPickRadius, bankPile.EffectiveDiameter * 0.65f);
        var lane = -1;
        var nearest = float.MaxValue;
        for (var index = 0; index < bank.Count; index++)
        {
            if (bank[index].Count <= 0)
                continue;

            var x = LaneOffset(index, bank.Count, bankPile.StackSpacing);
            var distance = Mathf.Abs(local.X - x);
            if (distance > radius || distance >= nearest)
                continue;

            lane = index;
            nearest = distance;
        }

        if (lane < 0 || Mathf.Abs(local.Z) > radius * 1.25f)
            return false;

        return TrySelectPreparedLane(playerId, lane, out denomination);
    }

    /// <summary>
    /// Builds an exact physical call from the same stable bank used by manual selection. It creates
    /// the ordinary prepared actors one by one, so replication, sound and the later push all reuse
    /// the established path instead of inventing a shortcut-only visual.
    /// </summary>
    public bool TryPrepareAutomaticWager(string playerId, int amount)
    {
        if (_preparedSubmitted || amount <= 0 || PreparedWagerAmount > 0
            || _preparedChips.Any(chip => chip.Returning)
            || !IsLocalWagerPlayer(playerId)
            || !_bankRuns.TryGetValue(playerId, out var bank))
            return false;

        var probe = bank.Select(run => new ChipRun(run.Denomination, run.Count)).ToList();
        if (!PokerChipStack.TryTake(probe, amount, out var payment))
            return false;

        foreach (var run in payment)
        {
            for (var chip = 0; chip < run.Count; chip++)
            {
                var lane = bank.FindIndex(candidate =>
                    candidate.Denomination == run.Denomination && candidate.Count > 0);
                if (lane >= 0 && TrySelectPreparedLane(playerId, lane, out _))
                    continue;

                RestorePreparedImmediately();
                return false;
            }
        }

        return PreparedWagerAmount == amount;
    }

    public bool PreparedWagerReady => PreparedWagerChipCount > 0
        && _preparedChips.Where(chip => chip.Included)
            .All(chip => !chip.Returning && chip.Progress >= 1.0f);

    private bool TrySelectPreparedLane(string playerId, int lane, out int denomination)
    {
        denomination = 0;
        if (!_stacks.TryGetValue(playerId, out var bankPile)
            || !_bankRuns.TryGetValue(playerId, out var bank)
            || lane < 0 || lane >= bank.Count || bank[lane].Count <= 0)
            return false;

        var run = bank[lane];
        denomination = run.Denomination;
        bank[lane] = new ChipRun(run.Denomination, run.Count - 1);
        bankPile.SetRuns(bank);

        if (!TryPreparedPlaces(playerId, lane, denomination,
                out var basis, out var source, out var target, out var betSlot))
        {
            bank[lane] = run;
            bankPile.SetRuns(bank);
            denomination = 0;
            return false;
        }

        var actor = NewPreparedChip(run.Denomination, bankPile.Basis, source);
        var motionSeed = run.Denomination * 17 + lane * 31 + betSlot * 53;
        var restBasis = basis
            * new Basis(Vector3.Up, PokerChipPile.Noise(motionSeed, 61) * 0.42f);
        _preparedPlayerId = playerId;
        _preparedChips.Add(new PreparedChip
        {
            Pile = actor,
            Denomination = run.Denomination,
            Lane = lane,
            From = source,
            To = target,
            FromBasis = bankPile.Basis,
            ToBasis = restBasis,
            MotionSeed = motionSeed,
            BetSlot = betSlot,
            Progress = 0.0f,
        });
        return true;
    }

    /// <summary>Returns the upper selected chip under the aim to its original denomination lane.</summary>
    public bool TryReturnPreparedChip(
        string playerId, Vector2 boardAim, out int denomination)
    {
        denomination = 0;
        if (_preparedSubmitted || !IsLocalWagerPlayer(playerId)
            || playerId != _preparedPlayerId || BoardPresenter == null)
            return false;

        var aim = BoardAimInPresenter(boardAim);
        PreparedChip selected = null;
        var nearest = float.MaxValue;
        var highest = float.MinValue;
        for (var index = _preparedChips.Count - 1; index >= 0; index--)
        {
            var chip = _preparedChips[index];
            if (!chip.Included)
                continue;

            var position = chip.Pile.Position;
            var distance = new Vector2(position.X, position.Z).DistanceTo(aim);
            if (distance > PreparedChipPickRadius * 1.35f)
                continue;

            // A ray aimed at an imperfect stack must take the physically exposed top chip. Choosing
            // only the closest projected centre could remove a supporting chip and leave another one
            // floating above the table.
            var height = chip.Pile.Position.Y;
            if (height < highest - 0.0001f
                || (Mathf.IsEqualApprox(height, highest) && distance >= nearest))
                continue;

            selected = chip;
            highest = height;
            nearest = distance;
        }

        if (selected == null)
            return false;

        denomination = selected.Denomination;
        BeginPreparedReturn(selected);
        return true;
    }

    public bool SubmitPreparedWager(string playerId, int amount)
    {
        if (!IsLocalWagerPlayer(playerId) || playerId != _preparedPlayerId
            || amount <= 0 || amount != PreparedWagerAmount
            || _preparedChips.Any(chip => chip.Returning))
            return false;

        _preparedSubmitted = true;
        return true;
    }

    /// <summary>Restores every tentative chip. Used for fold, rejection, timeout and leaving.</summary>
    public void CancelPreparedWager(bool immediate = false)
    {
        _preparedSubmitted = false;
        if (immediate)
        {
            RestorePreparedImmediately();
            return;
        }

        foreach (var chip in _preparedChips.ToArray())
        {
            if (chip.Included)
                BeginPreparedReturn(chip);
        }
    }

    private bool AdvancePreparedWager(float delta)
    {
        var moved = false;
        for (var index = _preparedChips.Count - 1; index >= 0; index--)
        {
            var chip = _preparedChips[index];
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

            if (chip.Progress < 1.0f || !chip.Returning)
                continue;

            RestorePreparedChipToBank(chip);
            chip.Pile.Visible = false;
            chip.Pile.QueueFree();
            _preparedChips.RemoveAt(index);
        }

        if (_preparedChips.Count == 0)
            _preparedPlayerId = "";
        return moved;
    }

    private void BeginPreparedReturn(PreparedChip chip)
    {
        if (chip == null || !chip.Included || chip.Returning)
            return;

        chip.Included = false;
        chip.Returning = true;
        chip.Progress = 0.0f;
        chip.From = chip.Pile.Position;
        chip.To = ReturnTarget(chip.Lane, chip.Denomination);
        chip.FromBasis = chip.Pile.Basis;
        chip.ToBasis = _stacks.TryGetValue(_preparedPlayerId, out var bankPile)
            ? bankPile.Basis : Basis.Identity;
        chip.Pile.FlightProgress = 0.0f;
        RetargetIncludedPreparedChips();
    }

    private Vector3 ReturnTarget(int lane, int denomination)
    {
        if (!_stacks.TryGetValue(_preparedPlayerId, out var bankPile)
            || !_bankRuns.TryGetValue(_preparedPlayerId, out var bank)
            || lane < 0 || lane >= bank.Count
            || !TrySeatChipPlaces(_preparedPlayerId, BoardPresenter.Spec,
                out var basis, out var stack, out _))
            return Vector3.Zero;

        var returningBelow = _preparedChips.Count(chip =>
            chip.Returning && chip.Denomination == denomination);
        var height = bank[lane].Count + Mathf.Max(0, returningBelow - 1);
        var x = LaneOffset(lane, bank.Count, bankPile.StackSpacing);
        return stack + bankPile.Basis * new Vector3(
            x, height * bankPile.EffectiveThickness, 0.0f);
    }

    private bool TryPreparedPlaces(
        string playerId, int lane, int denomination,
        out Basis basis, out Vector3 source, out Vector3 target, out int betSlot)
    {
        basis = Basis.Identity;
        source = Vector3.Zero;
        target = Vector3.Zero;
        betSlot = 0;
        if (!_stacks.TryGetValue(playerId, out var bankPile)
            || !_bankRuns.TryGetValue(playerId, out var bank)
            || lane < 0 || lane >= bank.Count
            || !TrySeatChipPlaces(playerId, BoardPresenter.Spec,
                out basis, out var stack, out var bet))
            return false;

        var x = LaneOffset(lane, bank.Count, bankPile.StackSpacing);
        source = stack + bankPile.Basis * new Vector3(
            x, bank[lane].Count * bankPile.EffectiveThickness, 0.0f);

        // Tentative chips join the committed blind/bet in a few short contact stacks. Their projected
        // discs are allowed to overlap; the contact layout raises every overlapping chip by its real
        // thickness, so the result is compact without ever occupying the same volume.
        betSlot = StablePreparedBaseBetSlot(playerId)
                  + _preparedChips.Count(chip => chip.Included);
        target = bet + basis * PokerChipContactLayout.RootOffset(
            betSlot, bankPile.EffectiveDiameter, bankPile.EffectiveThickness);
        return true;
    }

    private int StablePreparedBaseBetSlot(string playerId)
    {
        if (_game == null || string.IsNullOrEmpty(playerId))
            return 0;
        return PokerChipStack.ChipCount(_game.RoundChipsOf(playerId));
    }

    /// <summary>
    /// Removing a tentative chip compacts the surviving selection back into snapshot order. This
    /// makes the local layout and every replicated peer derive identical contact slots.
    /// </summary>
    private void RetargetIncludedPreparedChips()
    {
        if (string.IsNullOrEmpty(_preparedPlayerId) || BoardPresenter == null
            || !_stacks.TryGetValue(_preparedPlayerId, out var bankPile)
            || !TrySeatChipPlaces(_preparedPlayerId, BoardPresenter.Spec,
                out var basis, out _, out var bet))
            return;

        var slot = StablePreparedBaseBetSlot(_preparedPlayerId);
        foreach (var chip in _preparedChips.Where(chip => chip.Included))
        {
            var target = bet + basis * PokerChipContactLayout.RootOffset(
                slot, bankPile.EffectiveDiameter, bankPile.EffectiveThickness);
            if (chip.BetSlot != slot || chip.To.DistanceTo(target) > 0.0001f)
            {
                chip.BetSlot = slot;
                chip.From = chip.Pile.Position;
                chip.To = target;
                chip.FromBasis = chip.Pile.Basis;
                chip.MotionSeed = chip.Denomination * 17 + chip.Lane * 31 + slot * 53;
                chip.ToBasis = basis * new Basis(Vector3.Up,
                    PokerChipPile.Noise(chip.MotionSeed, 61) * 0.42f);
                chip.Progress = 0.0f;
                chip.Pile.FlightProgress = 0.0f;
            }
            slot++;
        }
    }

    private PokerChipPile NewPreparedChip(int denomination, Basis basis, Vector3 source)
    {
        var pile = new PokerChipPile
        {
            Name = $"PreparedChip{_preparedChips.Count}",
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

    private List<PreparedTransfer> TakeSubmittedPreparedWager(string playerId, int amount)
    {
        if (!_preparedSubmitted || playerId != _preparedPlayerId || amount != PreparedWagerAmount)
            return new List<PreparedTransfer>();

        var transfers = _preparedChips.Where(chip => chip.Included)
            .Select(chip => new PreparedTransfer(chip.Denomination, chip.Pile, chip.BetSlot))
            .ToList();
        _preparedSubmitted = false;
        return transfers;
    }

    /// <summary>
    /// The animator adopted the exact tentative actors, so they are no longer local preparation state.
    /// Clearing only the bookkeeping preserves their node identity and their final selected positions.
    /// </summary>
    private void CompletePreparedWagerAdoption()
    {
        _preparedChips.Clear();
        _preparedPlayerId = "";
    }

    private void ReleaseConsumedPreparedWager(IReadOnlyList<PreparedTransfer> transfers)
    {
        if (transfers == null || transfers.Count == 0)
            return;

        foreach (var chip in _preparedChips)
        {
            chip.Pile.Visible = false;
            chip.Pile.QueueFree();
        }
        _preparedChips.Clear();
        _preparedPlayerId = "";
    }

    private void RestorePreparedImmediately()
    {
        if (_bankRuns.TryGetValue(_preparedPlayerId, out var bank))
        {
            foreach (var chip in _preparedChips)
                AddOne(bank, chip.Denomination);
            if (_stacks.TryGetValue(_preparedPlayerId, out var pile))
                pile.SetRuns(bank);
        }

        foreach (var chip in _preparedChips)
        {
            chip.Pile.Visible = false;
            chip.Pile.QueueFree();
        }
        _preparedChips.Clear();
        _preparedPlayerId = "";
    }

    private void RestorePreparedChipToBank(PreparedChip chip)
    {
        if (!_bankRuns.TryGetValue(_preparedPlayerId, out var bank))
            return;
        AddOne(bank, chip.Denomination);
        if (_stacks.TryGetValue(_preparedPlayerId, out var pile))
            pile.SetRuns(bank);
    }

    private Vector2 BoardAimInPresenter(Vector2 boardAim)
    {
        var world = BoardPresenter.ToGlobal(new Vector3(boardAim.X, 0.0f, boardAim.Y));
        var local = ToLocal(world);
        return new Vector2(local.X, local.Z);
    }

    private static float LaneOffset(int lane, int laneCount, float spacing) =>
        (lane - (laneCount - 1) * 0.5f) * spacing;

    private bool IsLocalWagerPlayer(string playerId) =>
        !string.IsNullOrEmpty(playerId) && _game?.Player != null
        && playerId == (string)_game.Player.Name;

    private static void AddOne(List<ChipRun> bank, int denomination)
    {
        for (var lane = 0; lane < bank.Count; lane++)
        {
            if (bank[lane].Denomination != denomination)
                continue;
            bank[lane] = new ChipRun(denomination, bank[lane].Count + 1);
            return;
        }

        bank.Add(new ChipRun(denomination, 1));
        bank.Sort((left, right) => right.Denomination.CompareTo(left.Denomination));
    }
}
