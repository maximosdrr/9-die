using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

/// <summary>
/// Local, reversible chip preparation. These actors are deliberately not replicated: opponents
/// learn a wager only after it is pushed and accepted, just as they do through the existing action.
/// </summary>
public partial class PokerSeatPresenter : Node3D
{
    [ExportGroup("Local wager preparation")]
    /// <summary>
    /// A selected chip advances toward the centre while staying aligned with its denomination lane.
    /// This reads as "taken out of the stack" without spending a second lateral area on the table.
    /// </summary>
    [Export] public float PreparedWagerForwardInset = 0.065f;
    [Export] public float PreparedChipMoveSeconds = 0.28f;
    [Export] public float PreparedChipArc = 0.018f;
    [Export] public float PreparedChipPickRadius = 0.030f;

    private sealed class PreparedChip
    {
        public PokerChipPile Pile;
        public int Denomination;
        public int Lane;
        public Vector3 From;
        public Vector3 To;
        public float Progress;
        public bool Included = true;
        public bool Returning;
    }

    private readonly struct PreparedTransfer
    {
        public readonly ChipRun Run;
        public readonly Vector3 Position;

        public PreparedTransfer(int denomination, Vector3 position)
        {
            Run = new ChipRun(denomination, 1);
            Position = position;
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

        var point = stack + basis * new Vector3(
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

        var run = bank[lane];
        denomination = run.Denomination;
        bank[lane] = new ChipRun(run.Denomination, run.Count - 1);
        bankPile.SetRuns(bank);

        if (!TryPreparedPlaces(playerId, lane, denomination,
                out var basis, out var source, out var target))
        {
            bank[lane] = run;
            bankPile.SetRuns(bank);
            denomination = 0;
            return false;
        }

        var actor = NewPreparedChip(run.Denomination, basis, source);
        _preparedPlayerId = playerId;
        _preparedChips.Add(new PreparedChip
        {
            Pile = actor,
            Denomination = run.Denomination,
            Lane = lane,
            From = source,
            To = target,
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
        for (var index = _preparedChips.Count - 1; index >= 0; index--)
        {
            var chip = _preparedChips[index];
            if (!chip.Included)
                continue;

            var position = chip.Pile.Position;
            var distance = new Vector2(position.X, position.Z).DistanceTo(aim);
            if (distance > PreparedChipPickRadius * 1.35f || distance >= nearest)
                continue;

            selected = chip;
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

            chip.Progress = Mathf.Min(1.0f, chip.Progress
                + Mathf.Max(0.0f, delta) / Mathf.Max(PreparedChipMoveSeconds, 0.01f));
            chip.Pile.Position = PokerMotion.ChipThrow(
                chip.From, chip.To, chip.Progress, PreparedChipArc,
                PokerChipPile.Noise(chip.Lane + chip.Denomination, 47) * 0.005f);
            chip.Pile.FlightProgress = chip.Progress;
            moved = true;

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
        chip.Pile.FlightProgress = 0.0f;
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
        return stack + basis * new Vector3(
            x, height * bankPile.EffectiveThickness, 0.0f);
    }

    private bool TryPreparedPlaces(
        string playerId, int lane, int denomination,
        out Basis basis, out Vector3 source, out Vector3 target)
    {
        basis = Basis.Identity;
        source = Vector3.Zero;
        target = Vector3.Zero;
        if (!_stacks.TryGetValue(playerId, out var bankPile)
            || !_bankRuns.TryGetValue(playerId, out var bank)
            || lane < 0 || lane >= bank.Count
            || !TrySeatChipPlaces(playerId, BoardPresenter.Spec,
                out basis, out var stack, out var bet))
            return false;

        var x = LaneOffset(lane, bank.Count, bankPile.StackSpacing);
        source = stack + basis * new Vector3(
            x, bank[lane].Count * bankPile.EffectiveThickness, 0.0f);

        // Tentative chips join the already committed blind/bet instead of occupying a second wager
        // area. A small deterministic loose offset keeps every chip clickable and readable.
        var slot = NextBetLooseSlot(playerId) + _preparedChips.Count(chip => chip.Included);
        var angle = slot * 2.399963f;
        var radius = 0.010f + Mathf.Sqrt(slot + 1.0f) * 0.006f;
        target = bet + basis * new Vector3(
            Mathf.Cos(angle) * radius,
            (slot % 3) * bankPile.EffectiveThickness * 0.35f,
            Mathf.Sin(angle) * radius);
        return true;
    }

    private PokerChipPile NewPreparedChip(int denomination, Basis basis, Vector3 source)
    {
        var pile = new PokerChipPile
        {
            Name = $"PreparedChip{_preparedChips.Count}",
            ChipScene = _game?.ChipScene,
            CombineRunsIntoColumns = true,
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
            .Select(chip => new PreparedTransfer(chip.Denomination, chip.Pile.Position))
            .ToList();
        _preparedSubmitted = false;
        return transfers;
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
