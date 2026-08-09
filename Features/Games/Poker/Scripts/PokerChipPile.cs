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

	private readonly List<Node3D> _chips = new();
	private readonly List<MeshInstance3D> _bodies = new();
	private CylinderMesh _fallbackMesh;

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

	public int Amount { get; private set; }

	/// <summary>How tall one chip actually is once drawn — what the stacking steps by.</summary>
	public float EffectiveThickness =>
		PokerChipMeshes.IsAvailable ? PokerChipMeshes.Thickness : ChipThickness;

	/// <summary>Lays out <paramref name="amount"/> as chips. Zero leaves nothing on the cloth.</summary>
	public void Show(int amount)
	{
		Amount = Mathf.Max(0, amount);

		var runs = PokerChipStack.Decompose(Amount, MaxDenominations);
		var step = EffectiveThickness;
		var used = 0;

		for (var stack = 0; stack < runs.Count; stack++)
		{
			var run = runs[stack];
			var drawn = Mathf.Min(run.Count, MaxChipsPerStack);

			for (var height = 0; height < drawn; height++)
			{
				var chip = ChipAt(used);
				chip.Position = new Vector3(
					(stack - (runs.Count - 1) * 0.5f) * StackSpacing,
					(height + 0.5f) * step,
					0.0f);

				Tint(used, run.Denomination);
				chip.Visible = true;
				used++;
			}
		}

		for (var spare = used; spare < _chips.Count; spare++)
			_chips[spare].Visible = false;
	}

	public void Clear() => Show(0);

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
