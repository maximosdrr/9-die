using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

/// <summary>
/// Owns the persistent physical chip actors used by the poker presentation.
///
/// Rules never enter here: a batch only knows which physical chips it contains and which visual
/// phase it is traversing. PokerSeatPresenter decides when table stages begin; this component owns
/// pooling, identity preservation, chip trajectories, landing, pot organization and diagnostics.
/// </summary>
[GlobalClass]
public partial class PokerChipAnimator : Node3D
{
    public enum Phase
    {
        Unused,
        ToBet,
        PushingBet,
        Landing,
        AtBet,
        ToPot,
        AtPotLoose,
        Organizing,
        InPot,
        ToDealer,
        AtDealer,
        ToWinner,
        AtWinnerLoose,
        OrganizingWinner,
        AtWinner,
    }

    public sealed class Batch
    {
        public PokerChipPile Pile;
        /// <summary>
        /// The prewarmed pile that belongs to this pool slot. A prepared physical actor temporarily
        /// replaces <see cref="Pile"/>, but this reserve remains alive for allocation-free reuse.
        /// </summary>
        public PokerChipPile PoolPile;
        public StringName PoolName;
        public Phase Phase;
        public string PlayerId = "";
        public int Amount;
        public float Progress;
        public float Delay;
        public float Duration;
        public Vector3 From;
        public Vector3 To;
        public Vector3 OrganizeFrom;
        public Vector3 OrganizeTo;
        public float StartSpread;
        public Basis Basis = Basis.Identity;
        public int Sequence;
        public bool JustStarted;
        public string WinnerId = "";
        public Basis FromBasis = Basis.Identity;
        public Basis ToBasis = Basis.Identity;
    }

    private readonly List<Batch> _batches = new();
    private PackedScene _chipScene;
    private float _scatter;
    private int _prewarmedChipsPerBatch = 1;
    private float _flightSeconds;
    private float _flightArc;
    private float _landingSeconds;
    private float _collectSeconds;
    private float _organizeSeconds;
    private float _payoutSeconds;
    private float _dealerChangeSeconds;

    public IReadOnlyList<Batch> Batches => _batches;
    public event Action<Vector3, int> ChipsLanded;

    public void Configure(
        PackedScene chipScene, float scatter, int poolSize, int chipsPerBatch,
        float flightSeconds, float flightArc, float landingSeconds,
        float collectSeconds, float organizeSeconds, float payoutSeconds,
        float dealerChangeSeconds)
    {
        _chipScene = chipScene;
        _scatter = scatter;
        _prewarmedChipsPerBatch = Mathf.Max(1, chipsPerBatch);
        _flightSeconds = flightSeconds;
        _flightArc = flightArc;
        _landingSeconds = landingSeconds;
        _collectSeconds = collectSeconds;
        _organizeSeconds = organizeSeconds;
        _payoutSeconds = payoutSeconds;
        _dealerChangeSeconds = dealerChangeSeconds;
        for (var i = _batches.Count; i < Mathf.Max(1, poolSize); i++)
            _batches.Add(BuildBatch(i));
    }

    public Batch Acquire()
    {
        foreach (var batch in _batches)
        {
            if (batch.Phase == Phase.Unused)
                return batch;
        }

        // Normal play remains allocation-free. This fallback deliberately stays safe for custom rules
        // with more denominations than the configured visual budget.
        var extra = BuildBatch(_batches.Count);
        _batches.Add(extra);
        return extra;
    }

    /// <summary>
    /// Promotes a locally prepared chip into the persistent animation pool without replacing its mesh
    /// or moving it. The pooled placeholder is retired; from this point on the same physical actor can
    /// be collected into the pot and paid to a winner.
    /// </summary>
    public void Adopt(Batch batch, PokerChipPile pile)
    {
        if (batch == null || pile == null || batch.Pile == pile)
            return;

        var placeholder = batch.PoolPile ?? batch.Pile;
        var poolName = string.IsNullOrEmpty(batch.PoolName.ToString())
            ? placeholder?.Name ?? new StringName($"ChipBatch{_batches.IndexOf(batch)}")
            : batch.PoolName;
        if (placeholder != null)
        {
            placeholder.Visible = false;
            placeholder.Name = $"{poolName}_Reserve";
        }

        if (pile.GetParent() != this)
            pile.Reparent(this, true);
        pile.Name = poolName;
        batch.Pile = pile;
    }

    public void ResetAll()
    {
        foreach (var batch in _batches)
            Reset(batch);
    }

    public void Reset(Batch batch)
    {
        if (batch?.Pile == null)
            return;

        // Hide first: the actor may still be parked at a previous destination.
        batch.Pile.Visible = false;
        if (batch.PoolPile != null && batch.Pile != batch.PoolPile)
        {
            var released = batch.Pile;
            released.Clear();
            released.Name = $"{batch.PoolName}_Released";
            released.QueueFree();
            batch.Pile = batch.PoolPile;
            batch.Pile.Name = batch.PoolName;
        }
        batch.Phase = Phase.Unused;
        batch.PlayerId = "";
        batch.Amount = 0;
        batch.Progress = 0.0f;
        batch.Delay = 0.0f;
        batch.Duration = 0.0f;
        batch.Sequence = -1;
        batch.JustStarted = false;
        batch.WinnerId = "";
        batch.Basis = Basis.Identity;
        batch.FromBasis = Basis.Identity;
        batch.ToBasis = Basis.Identity;
        batch.StartSpread = 0.0f;
        batch.OrganizeFrom = Vector3.Zero;
        batch.OrganizeTo = Vector3.Zero;
        batch.Pile.FlightProgress = 1.0f;
        batch.Pile.Spread = 0.0f;
        batch.Pile.LooseSlotOffset = 0;
        batch.Pile.Clear();
        batch.Pile.Transform = Transform3D.Identity;
    }

    public bool HasPhase(params Phase[] phases)
    {
        foreach (var batch in _batches)
        {
            foreach (var phase in phases)
            {
                if (batch.Phase == phase)
                    return true;
            }
        }
        return false;
    }

    public IReadOnlyList<ulong> ActiveVisualIds()
    {
        var ids = new List<ulong>();
        foreach (var batch in _batches)
        {
            if (batch.Phase == Phase.Unused || batch.Pile == null)
                continue;
            ids.AddRange(batch.Pile.GetChildren().OfType<Node3D>()
                .Where(visual => visual.Visible).Select(visual => visual.GetInstanceId()));
        }
        return ids;
    }

    public IReadOnlyDictionary<ulong, Vector3> ActiveVisualPositions()
    {
        var positions = new Dictionary<ulong, Vector3>();
        foreach (var batch in _batches)
        {
            if (batch.Phase == Phase.Unused || batch.Pile == null)
                continue;
            foreach (var child in batch.Pile.GetChildren())
            {
                if (child is Node3D { Visible: true } visual)
                    positions[visual.GetInstanceId()] = batch.Pile.Transform * visual.Position;
            }
        }
        return positions;
    }

    public int ActiveBatchCount => _batches.Count(batch => batch.Phase != Phase.Unused);

    /// <summary>Advances one actor and reports whether it occupied this presentation frame.</summary>
    public bool Advance(Batch batch, float delta)
    {
        if (batch == null)
            return false;

        switch (batch.Phase)
        {
            case Phase.ToBet:
                AdvanceToBet(batch, delta);
                return true;
            case Phase.PushingBet:
                AdvancePushingBet(batch, delta);
                return true;
            case Phase.Landing:
                AdvanceLanding(batch, delta);
                return true;
            case Phase.ToPot:
                AdvanceToPot(batch, delta);
                return true;
            case Phase.Organizing:
                AdvanceOrganization(batch, delta);
                return true;
            case Phase.ToDealer:
                AdvanceTransfer(batch, delta, Phase.AtDealer, 0.035f,
                    _dealerChangeSeconds, targetSpread: 0.0f);
                return true;
            case Phase.ToWinner:
                AdvanceTransfer(batch, delta, Phase.AtWinnerLoose, 0.055f,
                    batch.Duration > 0.0f ? batch.Duration : _payoutSeconds,
                    targetSpread: 1.0f);
                return true;
            case Phase.OrganizingWinner:
                AdvanceWinnerOrganization(batch, delta);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Assigns every loose pot actor to its denomination column.</summary>
    public bool BeginOrganization(Vector3 centre, Basis reader, float columnSpacing)
    {
        var potBatches = _batches.Where(batch =>
            batch.Phase is Phase.AtPotLoose or Phase.InPot)
            .OrderBy(batch => batch.Sequence).ToList();
        if (potBatches.Count == 0)
            return false;

        var denominations = potBatches.Select(DenominationOf).Where(value => value > 0)
            .Distinct().OrderByDescending(value => value).ToList();
        var heights = denominations.ToDictionary(value => value, _ => 0.0f);
        foreach (var batch in potBatches)
        {
            var denomination = DenominationOf(batch);
            var lane = Mathf.Max(0, denominations.IndexOf(denomination));
            var x = (lane - (denominations.Count - 1) * 0.5f) * columnSpacing;
            batch.OrganizeFrom = batch.Pile.Position;
            batch.OrganizeTo = centre + reader * new Vector3(
                x, heights.GetValueOrDefault(denomination), 0.0f);
            batch.FromBasis = batch.Pile.Basis;
            batch.ToBasis = reader;
            batch.StartSpread = batch.Pile.Spread;
            batch.Progress = 0.0f;
            batch.Phase = Phase.Organizing;
            heights[denomination] = heights.GetValueOrDefault(denomination) + batch.Pile.TopHeight;
        }
        return true;
    }

    public static int DenominationOf(Batch batch) =>
        batch?.Pile?.Runs.Count > 0 ? batch.Pile.Runs[0].Denomination : 0;

    /// <summary>
    /// Splits a physical payment into a bounded number of independently animated groups. A group
    /// never mixes denominations, so organized pots keep one honest column per chip type. Small
    /// payments still return one group per chip and therefore retain the detailed motion.
    /// </summary>
    public static List<ChipRun> GroupRuns(IReadOnlyList<ChipRun> runs, int groupBudget)
    {
        var source = runs?.Where(run => run.Count > 0 && run.Denomination > 0).ToList()
            ?? new List<ChipRun>();
        if (source.Count == 0)
            return new List<ChipRun>();

        var physicalCount = source.Sum(run => run.Count);
        var budget = Mathf.Clamp(groupBudget, source.Count, physicalCount);
        if (budget >= physicalCount)
        {
            var individual = new List<ChipRun>(physicalCount);
            foreach (var run in source)
            {
                for (var chip = 0; chip < run.Count; chip++)
                    individual.Add(new ChipRun(run.Denomination, 1));
            }
            return individual;
        }

        var groupsPerRun = Enumerable.Repeat(1, source.Count).ToArray();
        for (var allocated = source.Count; allocated < budget; allocated++)
        {
            var best = 0;
            var bestLoad = -1.0f;
            for (var i = 0; i < source.Count; i++)
            {
                if (groupsPerRun[i] >= source[i].Count)
                    continue;
                var load = source[i].Count / (float)groupsPerRun[i];
                if (load > bestLoad)
                {
                    best = i;
                    bestLoad = load;
                }
            }
            groupsPerRun[best]++;
        }

        var grouped = new List<ChipRun>(budget);
        for (var i = 0; i < source.Count; i++)
        {
            var baseSize = source[i].Count / groupsPerRun[i];
            var remainder = source[i].Count % groupsPerRun[i];
            for (var group = 0; group < groupsPerRun[i]; group++)
                grouped.Add(new ChipRun(source[i].Denomination,
                    baseSize + (group < remainder ? 1 : 0)));
        }
        return grouped;
    }

    private void AdvanceToBet(Batch batch, float delta)
    {
        if (batch.JustStarted)
        {
            batch.JustStarted = false;
            batch.Pile.Transform = new Transform3D(batch.Basis, batch.From);
            return;
        }
        if (batch.Delay > 0.0f)
        {
            batch.Delay = Mathf.Max(0.0f, batch.Delay - delta);
            return;
        }

        batch.Progress = Mathf.Min(1.0f,
            batch.Progress + delta / Mathf.Max(_flightSeconds, 0.01f));
        var path = PokerMotion.ChipThrow(
            new Vector2(batch.From.X, batch.From.Z), new Vector2(batch.To.X, batch.To.Z),
            batch.Progress, _flightArc, PokerChipPile.Noise(batch.Amount, 14) * 0.010f);
        var wave = Mathf.Sin(batch.Progress * Mathf.Pi);
        var yaw = PokerChipPile.Noise(batch.Amount, 13) * 0.10f * wave;
        batch.Pile.Transform = new Transform3D(batch.Basis * new Basis(Vector3.Up, yaw), path);
        batch.Pile.FlightProgress = batch.Progress;
        batch.Pile.Spread = PokerMotion.Smooth(batch.Progress);
        if (batch.Progress < 1.0f)
            return;

        batch.Progress = 0.0f;
        batch.Phase = Phase.Landing;
        batch.Pile.FlightProgress = 1.0f;
        EmitLanding(batch);
    }

    /// <summary>
    /// Slides already prepared chips across the felt. Unlike ToBet this has no toss, tumble, bounce
    /// or re-scatter: it is the short physical push that confirms the player's intention.
    /// </summary>
    private static void AdvancePushingBet(Batch batch, float delta)
    {
        batch.Progress = Mathf.Min(1.0f,
            batch.Progress + delta / Mathf.Max(batch.Duration, 0.01f));
        var t = PokerMotion.Smooth(batch.Progress);
        var basis = new Transform3D(batch.FromBasis, Vector3.Zero).InterpolateWith(
            new Transform3D(batch.ToBasis, Vector3.Zero), t).Basis;
        batch.Pile.Transform = new Transform3D(basis, batch.From.Lerp(batch.To, t));
        batch.Pile.FlightProgress = 1.0f;
        if (batch.Progress >= 1.0f)
            batch.Phase = Phase.AtBet;
    }

    private void AdvanceLanding(Batch batch, float delta)
    {
        batch.Progress = Mathf.Min(1.0f,
            batch.Progress + delta / Mathf.Max(_landingSeconds, 0.01f));
        var decay = 1.0f - batch.Progress;
        var bounce = Mathf.Abs(Mathf.Sin(batch.Progress * Mathf.Pi * 2.0f)) * decay * 0.009f;
        batch.Pile.Transform = new Transform3D(batch.Basis, batch.To + Vector3.Up * bounce);
        if (batch.Progress >= 1.0f)
            batch.Phase = Phase.AtBet;
    }

    private void AdvanceToPot(Batch batch, float delta)
    {
        if (batch.Delay > 0.0f)
        {
            batch.Delay -= delta;
            return;
        }

        batch.Progress = Mathf.Min(1.0f,
            batch.Progress + delta / Mathf.Max(_collectSeconds, 0.01f));
        var wasContactStacked = batch.StartSpread < 0.999f;
        var spreadProgress = wasContactStacked
            ? Mathf.Clamp(batch.Progress / 0.28f, 0.0f, 1.0f)
            : 1.0f;
        batch.Pile.Spread = Mathf.Lerp(
            batch.StartSpread, 1.0f, PokerMotion.Smooth(spreadProgress));

        // First open a contact stack into disjoint loose slots, then sweep it. Moving every root to
        // the pot while Spread was still zero collapsed all adopted one-chip piles into one volume.
        // The Vector3 path also preserves each supporting layer's height until horizontal clearance
        // exists; the old Vector2 overload dropped stacked roots to Y=0 on its first moving frame.
        var travelProgress = wasContactStacked
            ? Mathf.Clamp((batch.Progress - 0.18f) / 0.82f, 0.0f, 1.0f)
            : batch.Progress;
        var path = PokerMotion.ChipThrow(
            batch.From, batch.To, travelProgress,
            0.028f, PokerChipPile.Noise(batch.Amount, 16) * 0.012f);
        var rotation = new Transform3D(batch.Basis, Vector3.Zero).InterpolateWith(
            new Transform3D(Basis.Identity, Vector3.Zero), PokerMotion.Smooth(batch.Progress)).Basis;
        batch.Pile.Transform = new Transform3D(rotation, path);
        batch.Pile.FlightProgress = batch.Progress;
        batch.Pile.LooseLayoutProgress = PokerMotion.Smooth(batch.Progress);
        if (batch.Progress >= 1.0f)
        {
            batch.Phase = Phase.AtPotLoose;
            batch.Pile.FlightProgress = 1.0f;
            batch.Pile.Spread = 1.0f;
            EmitLanding(batch);
        }
    }

    private void AdvanceOrganization(Batch batch, float delta)
    {
        batch.Progress = Mathf.Min(1.0f,
            batch.Progress + delta / Mathf.Max(_organizeSeconds, 0.01f));
        var t = PokerMotion.Smooth(batch.Progress);
        var basis = new Transform3D(batch.FromBasis, Vector3.Zero).InterpolateWith(
            new Transform3D(batch.ToBasis, Vector3.Zero), t).Basis;
        batch.Pile.Transform = new Transform3D(
            basis, batch.OrganizeFrom.Lerp(batch.OrganizeTo, t));
        batch.Pile.Spread = Mathf.Lerp(batch.StartSpread, 0.0f, t);
        if (batch.Progress >= 1.0f)
            batch.Phase = Phase.InPot;
    }

    private void AdvanceTransfer(
        Batch batch, float delta, Phase completedPhase, float arc, float duration,
        float targetSpread)
    {
        if (batch.JustStarted)
        {
            batch.JustStarted = false;
            batch.Pile.Transform = new Transform3D(batch.FromBasis, batch.From);
            return;
        }
        if (batch.Delay > 0.0f)
        {
            batch.Delay = Mathf.Max(0.0f, batch.Delay - delta);
            return;
        }

        batch.Progress = Mathf.Min(1.0f,
            batch.Progress + delta / Mathf.Max(duration, 0.01f));
        var t = PokerMotion.Smooth(batch.Progress);
        var position = PokerMotion.ChipThrow(batch.From, batch.To, batch.Progress,
            arc, PokerChipPile.Noise(batch.Sequence, 31) * 0.014f);
        var basis = new Transform3D(batch.FromBasis, Vector3.Zero).InterpolateWith(
            new Transform3D(batch.ToBasis, Vector3.Zero), t).Basis;
        batch.Pile.Transform = new Transform3D(basis, position);
        batch.Pile.FlightProgress = batch.Progress;
        batch.Pile.Spread = Mathf.Lerp(batch.StartSpread, targetSpread, t);
        if (batch.Progress < 1.0f)
            return;

        batch.Pile.Transform = new Transform3D(batch.ToBasis, batch.To);
        batch.Pile.FlightProgress = 1.0f;
        batch.Pile.Spread = targetSpread;
        batch.Phase = completedPhase;
        EmitLanding(batch);
    }

    /// <summary>
    /// Gathers a winner's loose delivery into denomination columns. This is a second physical phase,
    /// not a redraw of the bank, so every chip that crossed the cloth remains the same node.
    /// </summary>
    private void AdvanceWinnerOrganization(Batch batch, float delta)
    {
        if (batch.Delay > 0.0f)
        {
            batch.Delay = Mathf.Max(0.0f, batch.Delay - delta);
            return;
        }

        batch.Progress = Mathf.Min(1.0f,
            batch.Progress + delta / Mathf.Max(batch.Duration, 0.01f));
        var t = PokerMotion.Smooth(batch.Progress);
        var basis = new Transform3D(batch.FromBasis, Vector3.Zero).InterpolateWith(
            new Transform3D(batch.ToBasis, Vector3.Zero), t).Basis;
        batch.Pile.Transform = new Transform3D(
            basis, batch.OrganizeFrom.Lerp(batch.OrganizeTo, t));
        batch.Pile.Spread = Mathf.Lerp(batch.StartSpread, 0.0f, t);
        batch.Pile.FlightProgress = 1.0f;
        if (batch.Progress < 1.0f)
            return;

        batch.Pile.Transform = new Transform3D(batch.ToBasis, batch.OrganizeTo);
        batch.Pile.Spread = 0.0f;
        batch.Phase = Phase.AtWinner;
    }

    private void EmitLanding(Batch batch)
    {
        var chipCount = batch?.Pile?.ChipCount ?? 0;
        if (chipCount > 0)
            ChipsLanded?.Invoke(batch.Pile.GlobalPosition, chipCount);
    }

    private Batch BuildBatch(int index)
    {
        var pile = new PokerChipPile
        {
            Name = $"ChipBatch{index}",
            ChipScene = _chipScene,
            Scatter = _scatter,
            CombineRunsIntoColumns = true,
            LooseWhenSpread = true,
            Spread = 0.0f,
            SettleSeconds = 0.0f,
        };
        AddChild(pile);
        pile.Prewarm(_prewarmedChipsPerBatch);
        pile.Visible = false;
        return new Batch
        {
            Pile = pile,
            PoolPile = pile,
            PoolName = pile.Name,
            Phase = Phase.Unused,
        };
    }
}
