using System.Collections.Generic;
using Godot;

namespace Domino.Rules;

/// <summary>Where the face-down stock sits on the cloth, in metres, table-local.</summary>
public readonly struct BoneyardSpec
{
	public readonly float TileLength;
	public readonly float TileWidth;
	public readonly float Gap;

	/// <summary>Tiles per row before the stock wraps to the next one.</summary>
	public readonly int Columns;

	/// <summary>Table-local anchor of the first slot's row, away from the playing area.</summary>
	public readonly Vector2 Origin;

	public BoneyardSpec(float tileLength, float tileWidth, float gap, int columns, Vector2 origin)
	{
		TileLength = tileLength;
		TileWidth = tileWidth;
		Gap = gap;
		Columns = columns;
		Origin = origin;
	}

	public static BoneyardSpec Default =>
		new(0.048f, 0.024f, 0.004f, 7, new Vector2(0.0f, 0.30f));
}

/// <summary>
/// Lays the stock out as face-down tiles the player can actually pick from.
///
/// A slot's position depends ONLY on its number, never on which slots are still occupied. That is
/// what lets a drawn tile leave a gap behind instead of making the rest of the stock slide across
/// the table, and it is what keeps a slot number meaning the same thing between the moment the
/// player aims and the moment the server reads the request.
/// </summary>
public static class DominoBoneyardLayout
{
	/// <summary>Rows grow away from the playing area, so slot n never moves as the stock shrinks.</summary>
	public static Vector2 SlotPosition(int slot, BoneyardSpec spec)
	{
		var column = slot % spec.Columns;
		var row = slot / spec.Columns;

		var x = (column - (spec.Columns - 1) * 0.5f) * (spec.TileWidth + spec.Gap);
		var z = row * (spec.TileLength + spec.Gap);

		return spec.Origin + new Vector2(x, z);
	}

	/// <summary>Face-down tiles lie along the same axis as the rows, so the footprint is fixed.</summary>
	public static Vector2 HalfExtents(BoneyardSpec spec) =>
		new(spec.TileWidth * 0.5f, spec.TileLength * 0.5f);

	/// <summary>
	/// The occupied slot nearest the aim point, or <see cref="DominoTileId.NoEnd"/> when the stock
	/// is spent. Only occupied slots are considered — a gap left by an earlier draw is not a target.
	/// </summary>
	public static int NearestSlot(IReadOnlyList<int> occupiedSlots, BoneyardSpec spec, Vector2 aimLocal)
	{
		if (occupiedSlots == null || occupiedSlots.Count == 0)
			return DominoTileId.NoEnd;

		var best = DominoTileId.NoEnd;
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
	/// Furthest extent the stock reaches for a given size — used to check it clears the playing
	/// area and still fits on the table.
	/// </summary>
	public static Rect2 Bounds(int slotCount, BoneyardSpec spec)
	{
		if (slotCount <= 0)
			return new Rect2(spec.Origin, Vector2.Zero);

		var half = HalfExtents(spec);
		var first = SlotPosition(0, spec) - half;
		var bounds = new Rect2(first, Vector2.Zero);

		for (var slot = 0; slot < slotCount; slot++)
		{
			var centre = SlotPosition(slot, spec);
			bounds = bounds.Expand(centre - half).Expand(centre + half);
		}

		return bounds;
	}
}
