using System.Collections.Generic;
using Godot;

/// <summary>Where a grid of addressable places sits on a table surface, in metres, table-local.</summary>
public readonly struct SlotGridSpec
{
	/// <summary>Footprint along the axis rows grow on (table-local Z).</summary>
	public readonly float SlotLength;

	/// <summary>Footprint across a row (table-local X).</summary>
	public readonly float SlotWidth;

	public readonly float Gap;

	/// <summary>Places per row before the grid wraps to the next one.</summary>
	public readonly int Columns;

	/// <summary>Table-local anchor of the first row.</summary>
	public readonly Vector2 Origin;

	public SlotGridSpec(float slotLength, float slotWidth, float gap, int columns, Vector2 origin)
	{
		SlotLength = slotLength;
		SlotWidth = slotWidth;
		Gap = gap;
		Columns = columns;
		Origin = origin;
	}
}

/// <summary>
/// A grid of places on a table that a player can point at and a server can name.
///
/// A place's position depends ONLY on its number, never on which places are still occupied. That is
/// what lets one leave a gap behind instead of making everything else slide across the table, and
/// it is what keeps a place number meaning the same thing between the moment the player aims and
/// the moment the server reads the request — the number is the whole message, so the two ends
/// cannot disagree about what was pointed at.
///
/// Used for anything addressable and face-down: a domino stock, a tray of chip denominations, a
/// row of community cards.
/// </summary>
public static class SlotGrid
{
	/// <summary>No place — an empty grid, or an aim that found nothing.</summary>
	public const int NoSlot = -1;

	/// <summary>Rows grow away from the origin, so place n never moves as the grid empties.</summary>
	public static Vector2 SlotPosition(int slot, SlotGridSpec spec)
	{
		var columns = Mathf.Max(spec.Columns, 1);
		var column = slot % columns;
		var row = slot / columns;

		var x = (column - (columns - 1) * 0.5f) * (spec.SlotWidth + spec.Gap);
		var z = row * (spec.SlotLength + spec.Gap);

		return spec.Origin + new Vector2(x, z);
	}

	/// <summary>Occupants lie along the same axis as the rows, so the footprint is fixed.</summary>
	public static Vector2 HalfExtents(SlotGridSpec spec) =>
		new(spec.SlotWidth * 0.5f, spec.SlotLength * 0.5f);

	/// <summary>
	/// The occupied place nearest the aim point, or <see cref="NoSlot"/> when the grid is empty.
	/// Only occupied places are considered — a gap left behind is not a target.
	/// </summary>
	public static int NearestSlot(IReadOnlyList<int> occupiedSlots, SlotGridSpec spec, Vector2 aimLocal)
	{
		if (occupiedSlots == null || occupiedSlots.Count == 0)
			return NoSlot;

		var best = NoSlot;
		var bestDistance = float.MaxValue;

		foreach (var slot in occupiedSlots)
		{
			if (slot < 0)
				continue;

			var distance = aimLocal.DistanceSquaredTo(SlotPosition(slot, spec));
			if (distance >= bestDistance)
				continue;

			bestDistance = distance;
			best = slot;
		}

		return best;
	}

	/// <summary>
	/// Furthest extent the grid reaches at a given size — used to check it clears whatever else is
	/// on the cloth and still fits on the table.
	/// </summary>
	public static Rect2 Bounds(int slotCount, SlotGridSpec spec)
	{
		if (slotCount <= 0)
			return new Rect2(spec.Origin, Vector2.Zero);

		var half = HalfExtents(spec);
		var bounds = new Rect2(SlotPosition(0, spec) - half, Vector2.Zero);

		for (var slot = 0; slot < slotCount; slot++)
		{
			var centre = SlotPosition(slot, spec);
			bounds = bounds.Expand(centre - half).Expand(centre + half);
		}

		return bounds;
	}
}
