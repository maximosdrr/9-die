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
/// Geometry comes from <see cref="PokerChipMeshes"/> — a rim and a coloured body — and falls back to
/// a flat cylinder when the art pack is missing, so the table stays playable either way.
/// </summary>
[GlobalClass]
public partial class PokerChipPile : Node3D
{
	/// <summary>Used only by the fallback; the pack's own measurements win when it is present.</summary>
	[Export] public float ChipDiameter = 0.040f;
	[Export] public float ChipThickness = 0.0035f;

	/// <summary>Space between neighbouring stacks of different denominations.</summary>
	[Export] public float StackSpacing = 0.046f;

	/// <summary>
	/// Chips drawn per stack before it starts a new one beside it. A tower of forty is both silly
	/// and unreadable.
	/// </summary>
	[Export] public int MaxChipsPerStack = 12;

	/// <summary>How many denominations are drawn before the rest is folded into the last.</summary>
	[Export] public int MaxDenominations = 4;

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

	/// <summary>Where a chip is between its column and its resting place.</summary>
	private readonly struct Slot
	{
		public readonly int Column;
		public readonly int Height;
		public readonly int Denomination;

		public Slot(int column, int height, int denomination)
		{
			Column = column;
			Height = height;
			Denomination = denomination;
		}
	}

	private readonly List<Node3D> _chips = new();
	private readonly List<MeshInstance3D> _bodies = new();
	private readonly List<Slot> _slots = new();
	private readonly List<ChipRun> _runs = new();
	private readonly List<float> _settle = new();
	private CylinderMesh _fallbackMesh;
	private int _columns;
	private float _spread = 1.0f;
	private bool _settling;

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

	/// <summary>How tall one chip actually is once drawn — what the stacking steps by.</summary>
	public float EffectiveThickness =>
		PokerChipMeshes.IsAvailable ? PokerChipMeshes.Thickness : ChipThickness;

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
			return;

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
					_slots.Add(new Slot(_columns, chip, run.Denomination));

				placed += height;
				_columns++;
			}
		}

		while (_settle.Count < _slots.Count)
			_settle.Add(1.0f);

		// Chips that were not there a moment ago arrive by falling. Everything that was already on
		// the cloth is left alone, so an addition never disturbs the pile under it.
		if (SettleSeconds > 0.0f)
		{
			for (var i = before; i < _slots.Count; i++)
			{
				_settle[i] = 0.0f;
				_settling = true;
			}
		}

		Apply();
	}

	private void Apply()
	{
		var step = EffectiveThickness;
		var jitter = Scatter * _spread;
		var tilt = Mathf.DegToRad(TiltDegrees) * _spread;

		for (var i = 0; i < _slots.Count; i++)
		{
			var slot = _slots[i];
			var chip = ChipAt(i);

			var settled = i < _settle.Count ? _settle[i] : 1.0f;

			var place = new Vector3(
				(slot.Column - (_columns - 1) * 0.5f) * StackSpacing + Noise(i, 0) * jitter,
				(slot.Height + 0.5f) * step + DropCurve(settled) * SettleHeight,
				Noise(i, 1) * jitter);

			// A chip is round, so its own spin is free either way; the lean is what says it landed
			// on the ones underneath instead of being placed on them.
			var basis = new Basis(Vector3.Up, Noise(i, 2) * Mathf.Pi)
						* new Basis(Vector3.Right, Noise(i, 3) * tilt)
						* new Basis(Vector3.Forward, Noise(i, 4) * tilt);

			Tint(i, slot.Denomination);
			chip.Transform = new Transform3D(basis, place);
			chip.Visible = true;
		}

		for (var spare = _slots.Count; spare < _chips.Count; spare++)
			_chips[spare].Visible = false;
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
	/// One chip. With the pack it is a rim plus a body so the edge reads; without it, a single
	/// cylinder that at least stacks to the right height.
	/// </summary>
	private void Build()
	{
		var chip = new Node3D();
		AddChild(chip);
		_chips.Add(chip);

		if (PokerChipMeshes.IsAvailable)
		{
			var rim = new MeshInstance3D { Mesh = PokerChipMeshes.Rim, Scale = PokerChipMeshes.SourceScale };
			var body = new MeshInstance3D { Mesh = PokerChipMeshes.Body, Scale = PokerChipMeshes.SourceScale };

			chip.AddChild(rim);
			chip.AddChild(body);
			_bodies.Add(body);
			return;
		}

		var flat = new MeshInstance3D { Mesh = FallbackMesh() };
		chip.AddChild(flat);
		_bodies.Add(flat);
	}

	private CylinderMesh FallbackMesh()
	{
		// One mesh for every chip: they are all the same size, and only the material differs.
		_fallbackMesh ??= new CylinderMesh
		{
			TopRadius = ChipDiameter * 0.5f,
			BottomRadius = ChipDiameter * 0.5f,
			Height = ChipThickness,
			RadialSegments = 16,
			Rings = 1,
		};

		return _fallbackMesh;
	}

	private void Tint(int index, int denomination)
	{
		if (index >= _bodies.Count)
			return;

		var colour = Colours.TryGetValue(denomination, out var known)
			? known
			: new Color(0.5f, 0.5f, 0.5f);

		var body = _bodies[index];

		if (body.MaterialOverride is StandardMaterial3D material)
		{
			material.AlbedoColor = colour;
			return;
		}

		body.MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.55f };
	}
}
