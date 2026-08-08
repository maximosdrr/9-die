using System.Collections.Generic;
using Godot;

namespace Domino.Rules;

/// <summary>
/// Answers the two questions aiming asks: where would this tile land, and which end is the player
/// pointing at.
///
/// Both are answered by running the real layout on a hypothetical play rather than by new geometry.
/// That is the whole point — a preview computed any other way could disagree with where the tile
/// actually lands, and the player would watch a ghost lie to them.
/// </summary>
public static class DominoAim
{
	/// <summary>
	/// Where <paramref name="tileId"/> would end up if it were laid against <paramref name="end"/>
	/// right now. False when the move is not legal, so there is nothing to preview.
	/// </summary>
	public static bool TryPreviewPlacement(
		IReadOnlyList<PlayRecord> plays,
		LayoutSpec spec,
		int tileId,
		ChainEnd end,
		out TilePlacement placement)
	{
		placement = default;

		var board = DominoBoardState.FromPlays(plays);
		if (!DominoRules.CanPlace(tileId, end, board.LeftEnd, board.RightEnd))
			return false;

		// Replaying the chain with the candidate appended is what guarantees the ghost sits exactly
		// where the real tile will: same function, same inputs, one extra play.
		var hypothetical = new List<PlayRecord>(plays.Count + 1);
		hypothetical.AddRange(plays);
		hypothetical.Add(new PlayRecord(string.Empty, tileId, end));

		var layout = DominoChainLayout.Rebuild(hypothetical, spec);
		if (layout.Placements.Count != hypothetical.Count)
			return false;

		placement = layout.Placements[^1];
		return true;
	}

	/// <summary>
	/// Which open end the player is pointing at, judged by where the tile would actually go rather
	/// than by where the chain currently ends — those differ by half a tile, and at the moment of
	/// choosing it is the landing spot the player is looking at.
	///
	/// Falls back to the end the tile can legally take when only one works, and to
	/// <see cref="ChainEnd.Right"/> on an empty board, where there is nothing to choose between.
	/// </summary>
	public static ChainEnd NearestEnd(
		IReadOnlyList<PlayRecord> plays,
		LayoutSpec spec,
		int tileId,
		Vector2 aimLocal)
	{
		if (plays == null || plays.Count == 0)
			return ChainEnd.Right;

		var hasLeft = TryPreviewPlacement(plays, spec, tileId, ChainEnd.Left, out var left);
		var hasRight = TryPreviewPlacement(plays, spec, tileId, ChainEnd.Right, out var right);

		if (hasLeft && !hasRight)
			return ChainEnd.Left;

		if (hasRight && !hasLeft)
			return ChainEnd.Right;

		if (!hasLeft)
			return ChainEnd.Right;

		// Ties go right, deliberately and deterministically: the alternative is a preview that
		// flickers between ends while the player holds still on the midpoint.
		return aimLocal.DistanceSquaredTo(left.Center) < aimLocal.DistanceSquaredTo(right.Center)
			? ChainEnd.Left
			: ChainEnd.Right;
	}

	/// <summary>
	/// The pip a tile must lead with to be laid against an end — what the rotation has to match.
	/// <see cref="DominoTileId.NoEnd"/> on an empty board, where any half may lead.
	/// </summary>
	public static int RequiredLeadingPips(IReadOnlyList<PlayRecord> plays, ChainEnd end)
	{
		var board = DominoBoardState.FromPlays(plays);
		return board.IsEmpty ? DominoTileId.NoEnd : board.ValueAt(end);
	}
}
