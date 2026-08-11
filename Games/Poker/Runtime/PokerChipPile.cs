using System.Collections.Generic;
using Godot;
using Poker.Rules;

/// <summary>
/// A number of chips, drawn as the chips that make it up.
///
/// The pile has to be readable AS a pile: the denominations come from <see cref="PokerChipStack"/>,
/// which decomposes the way a dealer stacks it, and each value keeps the colour it has on a real
/// table. Counting the pile and reading the number must agree.
///
/// It also has to behave like chips rather than like a number. Two things do that. First, a pile
/// GROWS by having chips added to it — <see cref="AddUpTo"/> — instead of being redrawn from its
/// total, so the chips already lying there keep the value and colour they landed with. Second, a
/// chip that lands is knocked slightly out of true and drops the last of the way with a small
/// bounce, because chips let go of together do not stack themselves into a tower.
///
/// Every bit of that scatter is derived from the chip's own index, so it is identical on every peer
/// and stable from frame to frame. Nothing about it travels over the network.
///
/// Geometry normally comes from <see cref="PokerChipAssetMeshes"/>, with a different embossed mesh
/// for every value. The previous pack and a flat cylinder remain ordered fallbacks, so a missing art
/// import cannot make the table unplayable.
/// </summary>
[GlobalClass]
public partial class PokerChipPile : Node3D
{
    /// <summary>Used only by the fallback; the pack's own measurements win when it is present.</summary>
    [Export] public float ChipDiameter = 0.040f;
    [Export] public float ChipThickness = 0.0035f;

    /// <summary>
    /// Optional replaceable chip scene. Its root should be PokerChipVisual so denomination tinting is
    /// retained; a plain Node3D is still accepted when the art has baked colours.
    /// </summary>
    [Export] public PackedScene ChipScene;

    /// <summary>Space between neighbouring stacks of different denominations.</summary>
    [Export] public float StackSpacing = 0.046f;

    /// <summary>
    /// Chips drawn per stack before it starts a new one beside it. A tower of forty is both silly
    /// and unreadable.
    /// </summary>
    [Export] public int MaxChipsPerStack = 12;

    /// <summary>How many denominations are drawn before the rest is folded into the last.</summary>
    [Export] public int MaxDenominations = 4;

    /// <summary>
    /// Keeps one permanent column per run, including zero-count runs. Player banks use this so paying
    /// from one denomination never recentres or repaints every denomination beside it.
    /// </summary>
    [Export] public bool StableRunColumns;

    /// <summary>Combines all denominations into compact vertical columns.</summary>
    [Export] public bool CombineRunsIntoColumns;

    /// <summary>
    /// Interpolates from a compact stack at Spread=0 to chips lying independently at Spread=1.
    /// Action batches use it to land messy and later organize into a real tower.
    /// </summary>
    [Export] public bool LooseWhenSpread;

    /// <summary>
    /// First place occupied in the shared loose-chip layout. Several persistent batches may use the
    /// same root; disjoint ranges keep their chips from being drawn through one another.
    /// </summary>
    public int LooseSlotOffset
    {
        get => _looseSlotOffset;
        set
        {
            var clamped = Mathf.Max(0, value);
            if (clamped == _looseSlotOffset && _looseLayoutProgress >= 1.0f)
                return;
            _looseSlotOffsetFrom = clamped;
            _looseSlotOffset = clamped;
            _looseLayoutProgress = 1.0f;
            Apply();
        }
    }

    /// <summary>Progress from the previous shared range to <see cref="LooseSlotOffset"/>.</summary>
    public float LooseLayoutProgress
    {
        get => _looseLayoutProgress;
        set
        {
            var clamped = Mathf.Clamp(value, 0.0f, 1.0f);
            if (Mathf.IsEqualApprox(clamped, _looseLayoutProgress))
                return;
            _looseLayoutProgress = clamped;
            Apply();
        }
    }

    [ExportGroup("Settling")]
    /// <summary>
    /// How far a chip can end up from the middle of its stack, in metres. Zero draws a machine-perfect
    /// tower, which is right for chips a player has stacked themselves and wrong for chips that were
    /// thrown.
    /// </summary>
    [Export] public float Scatter;

    /// <summary>How far out of flat a landed chip can be knocked.</summary>
    [Export] public float TiltDegrees = 7.0f;

    /// <summary>How long a chip takes to fall the last of the way and stop moving. Zero: no drop.</summary>
    [Export] public float SettleSeconds;

    /// <summary>How far above its place a chip starts that fall.</summary>
    [Export] public float SettleHeight = 0.022f;

    /// <summary>Small breathing room between chips lying beside one another.</summary>
    [Export] public float LooseSpacingScale = 1.08f;

    /// <summary>Where a chip is between its column and its resting place.</summary>
    private readonly struct Slot
    {
        public readonly int Column;
        public readonly int Height;
        public readonly int Denomination;
        public readonly int VisualIndex;

        public Slot(int column, int height, int denomination, int visualIndex)
        {
            Column = column;
            Height = height;
            Denomination = denomination;
            VisualIndex = visualIndex;
        }
    }

    private readonly List<Node3D> _chips = new();
    private readonly List<PokerChipVisual> _visuals = new();
    private readonly List<Slot> _slots = new();
    private readonly List<ChipRun> _runs = new();
    private readonly List<float> _settle = new();
    private readonly List<List<int>> _stableLaneVisuals = new();
    private int _columns;
    private float _spread = 1.0f;
    private bool _settling;

    public override void _Ready()
    {
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        SetProcess(false);
    }
    private float _flightProgress = 1.0f;
    private int _looseSlotOffset;
    private int _looseSlotOffsetFrom;
    private float _looseLayoutProgress = 1.0f;

    /// <summary>Standard casino colours, so a value can be read from across the table.</summary>
    private static readonly Dictionary<int, Color> Colours = new()
    {
        [1] = new Color(0.93f, 0.93f, 0.90f),
        [5] = new Color(0.13f, 0.52f, 0.24f),
        [10] = new Color(0.15f, 0.34f, 0.74f),
        [25] = new Color(0.74f, 0.14f, 0.19f),
        [50] = new Color(0.16f, 0.16f, 0.19f),
        [100] = new Color(0.46f, 0.31f, 0.15f),
        [500] = new Color(0.11f, 0.31f, 0.39f),
        [1000] = new Color(0.56f, 0.08f, 0.15f),
        [5000] = new Color(0.45f, 0.13f, 0.56f),
        [10000] = new Color(0.09f, 0.09f, 0.11f),
    };

    /// <summary>What the pile was last asked to be worth.</summary>
    public int Amount { get; private set; }

    /// <summary>
    /// What the chips actually on the cloth add up to.
    ///
    /// Not always the same as <see cref="Amount"/>: capping the denominations rounds the tail UP, so
    /// a pile is occasionally worth a chip more than it was asked for. Growing measures against this
    /// rather than against the request, which is what stops the rounding compounding.
    /// </summary>
    public int Value => PokerChipStack.Total(_runs);

    /// <summary>The chips drawn right now, largest denomination first.</summary>
    public IReadOnlyList<ChipRun> Runs => _runs;

    /// <summary>How many chip objects are on the cloth.</summary>
    public int ChipCount => _slots.Count;

    /// <summary>Height occupied by the compact form, used to place persistent batches in one tower.</summary>
    public float TopHeight
    {
        get
        {
            var height = 0;
            foreach (var slot in _slots)
                height = Mathf.Max(height, slot.Height + 1);
            return height * EffectiveThickness;
        }
    }

    /// <summary>
    /// Builds reusable visuals before play begins. SetRuns then only reveals and moves these nodes,
    /// so an action does not allocate a replacement chip halfway through its trajectory.
    /// </summary>
    public void Prewarm(int count)
    {
        while (_chips.Count < Mathf.Max(0, count))
            Build();

        foreach (var chip in _chips)
            chip.Visible = false;
    }

    /// <summary>
    /// How far the pile is from a tidy column (0) to its settled scatter (1).
    ///
    /// The chips in flight ride this from 0 to 1 as they fall, so they leave the hand together and
    /// spread out on the way down.
    /// </summary>
    public float Spread
    {
        get => _spread;
        set
        {
            var clamped = Mathf.Clamp(value, 0.0f, 1.0f);
            if (Mathf.IsEqualApprox(clamped, _spread))
                return;

            _spread = clamped;
            Apply();
        }
    }

    /// <summary>
    /// Local animation progress while the pile is in the air. It adds a small independent lift and
    /// tumble per chip, so several chips no longer read as one rigid plastic cylinder.
    /// </summary>
    public float FlightProgress
    {
        get => _flightProgress;
        set
        {
            var clamped = Mathf.Clamp(value, 0.0f, 1.0f);
            if (Mathf.IsEqualApprox(clamped, _flightProgress))
                return;

            _flightProgress = clamped;
            Apply();
        }
    }

    /// <summary>How tall one chip actually is once drawn — what the stacking steps by.</summary>
    public float EffectiveThickness =>
        PokerChipAssetMeshes.IsAvailable ? PokerChipAssetMeshes.Thickness
        : PokerChipMeshes.IsAvailable ? PokerChipMeshes.Thickness
        : ChipThickness;

    /// <summary>Actual drawn diameter, including the scale of a replacement mesh.</summary>
    public float EffectiveDiameter =>
        PokerChipAssetMeshes.IsAvailable ? PokerChipAssetMeshes.Diameter
        : PokerChipMeshes.IsAvailable ? PokerChipMeshes.Diameter
        : ChipDiameter;

    /// <summary>
    /// Reassigns this batch into a shared layout without moving it on the calling frame. The caller
    /// advances <see cref="LooseLayoutProgress"/> together with the batch's physical travel.
    /// </summary>
    public void RetargetLooseSlots(int offset)
    {
        _looseSlotOffsetFrom = _looseLayoutProgress >= 1.0f
            ? _looseSlotOffset
            : _looseSlotOffsetFrom;
        _looseSlotOffset = Mathf.Max(0, offset);
        _looseLayoutProgress = 0.0f;
        Apply();
    }

    /// <summary>Lays out <paramref name="amount"/> as chips, from scratch. Zero leaves nothing.</summary>
    public void Show(int amount)
    {
        amount = Mathf.Max(0, amount);
        SetRuns(PokerChipStack.Decompose(amount, MaxDenominations));
        Amount = amount;
    }

    /// <summary>
    /// Brings the pile up to <paramref name="total"/> by ADDING the difference as new chips.
    ///
    /// This is the difference between chips and a number. Redrawing a growing pile from its total
    /// re-decomposes the whole thing, so pushing a 10 onto a 20 turned the two blue chips already
    /// lying on the cloth into a red 25 and a green 5 under the player's hand. Here the 20 stays the
    /// 20 that was thrown and the 10 lands on top of it.
    ///
    /// A pile that SHRINKS was swept away rather than paid out of, so it is simply redrawn.
    /// </summary>
    public void AddUpTo(int total)
    {
        total = Mathf.Max(0, total);

        if (total < Value)
        {
            Show(total);
            return;
        }

        var missing = total - Value;
        if (missing > 0)
        {
            var grown = new List<ChipRun>(_runs);
            Merge(grown, PokerChipStack.Decompose(missing, MaxDenominations));
            SetRuns(grown);
        }

        Amount = total;
    }

    /// <summary>
    /// Draws exactly these chips.
    ///
    /// The seam that lets chips keep their identity across two piles: the same run list that flew
    /// through the air is handed to the pile it lands in, so nothing changes value or colour at the
    /// moment it touches down.
    /// </summary>
    public void SetRuns(IReadOnlyList<ChipRun> runs)
    {
        if (SameAsDrawn(runs))
            return;

        _runs.Clear();
        if (runs != null)
            _runs.AddRange(runs);

        Amount = Value;
        Rebuild();
    }

    public void Clear()
    {
        SetRuns(System.Array.Empty<ChipRun>());
        Amount = 0;
    }

    public override void _Process(double delta)
    {
        if (!_settling)
        {
            SetProcess(false);
            return;
        }

        var step = SettleSeconds <= 0.0f ? 1.0f : (float)delta / SettleSeconds;
        _settling = false;

        for (var i = 0; i < _settle.Count; i++)
        {
            if (_settle[i] >= 1.0f)
                continue;

            _settle[i] = Mathf.Min(1.0f, _settle[i] + step);
            if (_settle[i] < 1.0f)
                _settling = true;
        }

        Apply();
        if (!_settling)
            SetProcess(false);
    }

    // ---------------------------------------------------------------- layout

    /// <summary>
    /// Turns the runs into one slot per chip, keeping every chip that was already drawn exactly where
    /// it was. Only the chips beyond the old count are new, and only those fall into place.
    /// </summary>
    private void Rebuild()
    {
        var before = _slots.Count;

        _slots.Clear();
        _columns = 0;

        if (StableRunColumns)
        {
            BuildStableRunSlots();
            FinishRebuild(before);
            return;
        }

        if (CombineRunsIntoColumns)
        {
            BuildCombinedSlots();
            FinishRebuild(before);
            return;
        }

        foreach (var run in _runs)
        {
            if (run.Count <= 0)
                continue;

            // A run taller than a stack is split into as many columns as it needs, evenly, rather
            // than filling one and dropping the rest on the floor.
            var columns = Mathf.Max(1, (run.Count + MaxChipsPerStack - 1) / MaxChipsPerStack);
            var placed = 0;

            for (var column = 0; column < columns; column++)
            {
                var height = (run.Count - placed) / (columns - column);

                for (var chip = 0; chip < height; chip++)
                    _slots.Add(new Slot(_columns, chip, run.Denomination, _slots.Count));

                placed += height;
                _columns++;
            }
        }

        FinishRebuild(before);
    }

    private void BuildStableRunSlots()
    {
        _columns = Mathf.Max(1, _runs.Count);
        while (_stableLaneVisuals.Count < _runs.Count)
            _stableLaneVisuals.Add(new List<int>());

        for (var lane = 0; lane < _runs.Count; lane++)
        {
            var run = _runs[lane];
            var visuals = _stableLaneVisuals[lane];
            while (visuals.Count < run.Count)
            {
                Build();
                visuals.Add(_chips.Count - 1);
            }

            for (var chip = 0; chip < run.Count; chip++)
                _slots.Add(new Slot(lane, chip, run.Denomination, visuals[chip]));
        }
    }

    private void BuildCombinedSlots()
    {
        var index = 0;
        foreach (var run in _runs)
        {
            for (var chip = 0; chip < run.Count; chip++)
            {
                var column = index / Mathf.Max(1, MaxChipsPerStack);
                var height = index % Mathf.Max(1, MaxChipsPerStack);
                _slots.Add(new Slot(column, height, run.Denomination, index));
                index++;
            }
        }

        _columns = Mathf.Max(1,
            (index + Mathf.Max(1, MaxChipsPerStack) - 1) / Mathf.Max(1, MaxChipsPerStack));
    }

    private void FinishRebuild(int before)
    {
        var requiredSettleSlots = 0;
        foreach (var slot in _slots)
            requiredSettleSlots = Mathf.Max(requiredSettleSlots, slot.VisualIndex + 1);

        while (_settle.Count < requiredSettleSlots)
            _settle.Add(1.0f);

        // Chips that were not there a moment ago arrive by falling. Everything that was already on
        // the cloth is left alone, so an addition never disturbs the pile under it.
        if (SettleSeconds > 0.0f)
        {
            for (var i = before; i < _slots.Count; i++)
            {
                _settle[_slots[i].VisualIndex] = 0.0f;
                _settling = true;
            }
        }

        SetProcess(_settling);

        Apply();
    }

    private void Apply()
    {
        var step = EffectiveThickness;
        var jitter = Scatter * _spread;
        var tilt = Mathf.DegToRad(TiltDegrees) * _spread;
        var used = new bool[_chips.Count];

        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            var chip = ChipAt(slot.VisualIndex);
            if (slot.VisualIndex >= used.Length)
                System.Array.Resize(ref used, _chips.Count);
            used[slot.VisualIndex] = true;

            var settled = slot.VisualIndex < _settle.Count ? _settle[slot.VisualIndex] : 1.0f;

            var stacked = new Vector3(
                (slot.Column - (_columns - 1) * 0.5f) * StackSpacing,
                (slot.Height + 0.5f) * step + DropCurve(settled) * SettleHeight,
                0.0f);
            Vector3 place;
            if (LooseWhenSpread)
            {
                // Random scatter used to place centres only a few millimetres apart even though a
                // chip is about 40 mm wide. A shared hexagonal lattice guarantees clearance while
                // the independent tilt/spin below preserves the hand-thrown appearance.
                var spacing = EffectiveDiameter * Mathf.Max(1.0f, LooseSpacingScale);
                var oldSlot = HexSlot(_looseSlotOffsetFrom + i, spacing);
                var newSlot = HexSlot(_looseSlotOffset + i, spacing);
                var slot2 = oldSlot.Lerp(newSlot, PokerMotion.Smooth(_looseLayoutProgress));
                var loose = new Vector3(
                    slot2.X,
                    (0.5f + i * 0.035f) * step + DropCurve(settled) * SettleHeight,
                    slot2.Y);
                place = stacked.Lerp(loose, PokerMotion.Smooth(_spread));
            }
            else
            {
                place = stacked + new Vector3(
                    Noise(slot.VisualIndex, 0) * jitter, 0.0f,
                    Noise(slot.VisualIndex, 1) * jitter);
            }

            // The flight root follows the average throw. Individual chips lag and lift by a few
            // millimetres, returning to the exact stacked transform at both endpoints.
            if (_flightProgress > 0.0f && _flightProgress < 1.0f)
            {
                var wave = Mathf.Sin(_flightProgress * Mathf.Pi);
                place.X += Noise(slot.VisualIndex, 5) * 0.0035f * wave;
                place.Y += (0.003f + Mathf.Abs(Noise(slot.VisualIndex, 6)) * 0.006f) * wave;
                place.Z += Noise(slot.VisualIndex, 7) * 0.0035f * wave;
            }

            // A chip is round, so its own spin is free either way; the lean is what says it landed
            // on the ones underneath instead of being placed on them.
            var basis = new Basis(Vector3.Up, Noise(slot.VisualIndex, 2) * Mathf.Pi)
                        * new Basis(Vector3.Right, Noise(slot.VisualIndex, 3) * tilt)
                        * new Basis(Vector3.Forward, Noise(slot.VisualIndex, 4) * tilt);

            if (_flightProgress > 0.0f && _flightProgress < 1.0f)
            {
                var tumble = Mathf.Sin(_flightProgress * Mathf.Pi);
                basis *= new Basis(Vector3.Right, Noise(slot.VisualIndex, 8) * 0.32f * tumble)
                         * new Basis(Vector3.Forward, Noise(slot.VisualIndex, 9) * 0.24f * tumble);
            }

            Tint(slot.VisualIndex, slot.Denomination);
            chip.Transform = new Transform3D(basis, place);
            chip.Visible = true;
        }

        for (var spare = 0; spare < _chips.Count; spare++)
        {
            if (spare >= used.Length || !used[spare])
                _chips[spare].Visible = false;
        }
    }

    /// <summary>
    /// Deterministic spiral over a hexagonal lattice. Consecutive or disjoint ranges can share one
    /// origin without overlaps, which lets separate action batches still look like one loose bet.
    /// </summary>
    public static Vector2 HexSlot(int index, float spacing)
    {
        if (index <= 0 || spacing <= 0.0f)
            return Vector2.Zero;

        var remaining = index - 1;
        var ring = 1;
        while (remaining >= 6 * ring)
        {
            remaining -= 6 * ring;
            ring++;
        }

        var q = ring;
        var r = 0;
        for (var side = 0; side < 6 && remaining > 0; side++)
        {
            var steps = Mathf.Min(ring, remaining);
            var direction = side switch
            {
                0 => new Vector2I(0, -1),
                1 => new Vector2I(-1, 0),
                2 => new Vector2I(-1, 1),
                3 => new Vector2I(0, 1),
                4 => new Vector2I(1, 0),
                _ => new Vector2I(1, -1),
            };
            q += direction.X * steps;
            r += direction.Y * steps;
            remaining -= steps;
        }

        return new Vector2(
            spacing * (q + r * 0.5f),
            spacing * 0.8660254f * r);
    }

    /// <summary>
    /// How far above its resting place a chip is, as a fraction of the drop, at
    /// <paramref name="t"/> through the settle — 0 just released, 1 at rest.
    ///
    /// It accelerates on the way down and then bounces once, small. A chip that stops dead reads as
    /// a chip that was PLACED; the bounce is the whole difference between that and one let go of.
    /// </summary>
    public static float DropCurve(float t)
    {
        const float Contact = 0.62f;
        const float BounceHeight = 0.18f;

        if (t >= 1.0f || t < 0.0f)
            return 0.0f;

        if (t < Contact)
        {
            var fall = t / Contact;
            return 1.0f - fall * fall;
        }

        var bounce = (t - Contact) / (1.0f - Contact);
        return BounceHeight * Mathf.Sin(bounce * Mathf.Pi) * (1.0f - bounce);
    }

    /// <summary>
    /// A stable number in roughly [-1, 1] for chip <paramref name="index"/>, channel
    /// <paramref name="channel"/>.
    ///
    /// Deterministic on purpose, and doubly so: a chip has to sit in the same place every frame, or
    /// the pile crawls, AND in the same place on every peer, or the scatter would have to be
    /// replicated. A hash of the index gives both for nothing.
    /// </summary>
    public static float Noise(int index, int channel)
    {
        unchecked
        {
            var hash = (uint)(index * 374761393 + channel * 668265263 + 1);
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            hash ^= hash >> 16;

            return hash / 2147483648.0f - 1.0f;
        }
    }

    /// <summary>Adds the chips in <paramref name="extra"/> to <paramref name="into"/>, largest first.</summary>
    private static void Merge(List<ChipRun> into, IReadOnlyList<ChipRun> extra)
    {
        foreach (var run in extra)
        {
            var found = false;

            for (var i = 0; i < into.Count; i++)
            {
                if (into[i].Denomination != run.Denomination)
                    continue;

                into[i] = new ChipRun(run.Denomination, into[i].Count + run.Count);
                found = true;
                break;
            }

            if (!found)
                into.Add(run);
        }

        into.Sort((left, right) => right.Denomination.CompareTo(left.Denomination));
    }

    private bool SameAsDrawn(IReadOnlyList<ChipRun> runs)
    {
        var count = runs?.Count ?? 0;
        if (count != _runs.Count)
            return false;

        for (var i = 0; i < count; i++)
        {
            if (runs[i].Denomination != _runs[i].Denomination || runs[i].Count != _runs[i].Count)
                return false;
        }

        return true;
    }

    // ---------------------------------------------------------------- the chips themselves

    private Node3D ChipAt(int index)
    {
        while (_chips.Count <= index)
            Build();

        return _chips[index];
    }

    /// <summary>
    /// One chip. Its scene owns appearance; this pile owns only physical arrangement.
    /// </summary>
    private void Build()
    {
        var chip = ChipScene?.Instantiate() as Node3D ?? new PokerChipVisual();
        AddChild(chip);
        _chips.Add(chip);
        _visuals.Add(chip as PokerChipVisual);
    }

    private void Tint(int index, int denomination)
    {
        if (index >= _visuals.Count)
            return;

        var colour = Colours.TryGetValue(denomination, out var known)
            ? known
            : new Color(0.5f, 0.5f, 0.5f);

        _visuals[index]?.Configure(denomination, colour);
    }
}
