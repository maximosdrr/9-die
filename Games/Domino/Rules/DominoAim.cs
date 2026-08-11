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
    /// Which open end the player is pointing at — decided by DISTANCE ALONE.
    ///
    /// It used to prefer whichever end could legally take the tile, which quietly helped the player
    /// and cost more than it gave: aiming at the near end and watching the preview jump to the far
    /// one reads as the game overriding you. Pointing somewhere now always previews there, and a
    /// tile that does not belong at that end simply refuses with a red frame.
    ///
    /// Judged by where the tile would LAND rather than by where the chain currently ends — those
    /// differ by half a tile, and the landing spot is what the player is looking at.
    /// </summary>
    public static ChainEnd NearestEnd(
        IReadOnlyList<PlayRecord> plays,
        LayoutSpec spec,
        int tileId,
        Vector2 aimLocal)
    {
        if (plays == null || plays.Count == 0)
            return ChainEnd.Right;

        var hasLeft = TryPreviewSlot(plays, spec, tileId, ChainEnd.Left, out var left);
        var hasRight = TryPreviewSlot(plays, spec, tileId, ChainEnd.Right, out var right);

        if (hasLeft && !hasRight)
            return ChainEnd.Left;

        if (!hasLeft)
            return ChainEnd.Right;

        // Ties go right, deliberately and deterministically: the alternative is a preview that
        // flickers between ends while the player holds still on the midpoint.
        return aimLocal.DistanceSquaredTo(left.Center) < aimLocal.DistanceSquaredTo(right.Center)
            ? ChainEnd.Left
            : ChainEnd.Right;
    }

    /// <summary>
    /// Where a tile of this shape WOULD land at this end, whether or not it is legal there.
    ///
    /// The player can pick any tile from their hand, so the preview has to be able to show an
    /// incompatible one sitting in the slot with a red border round it — that refusal is the
    /// feedback. Position depends only on the branch and on whether the tile is a double, so this
    /// replays the chain with a stand-in that IS legal and shares that shape, then swaps the face
    /// back in. Still the real layout, still one code path.
    /// </summary>
    public static bool TryPreviewSlot(
        IReadOnlyList<PlayRecord> plays,
        LayoutSpec spec,
        int tileId,
        ChainEnd end,
        out TilePlacement placement)
    {
        if (TryPreviewPlacement(plays, spec, tileId, end, out placement))
            return true;

        placement = default;
        if (!DominoTileId.IsValid(tileId))
            return false;

        var board = DominoBoardState.FromPlays(plays);
        var required = board.IsEmpty ? DominoTileId.NoEnd : board.ValueAt(end);
        if (required == DominoTileId.NoEnd)
            return false;

        // A double occupies its own footprint, so the stand-in has to match on that.
        var standIn = DominoTileId.IsDouble(tileId)
            ? DominoTileId.From(required, required)
            : DominoTileId.From(required, (required + 1) % (DominoTileId.MaxPips + 1));

        if (DominoTileId.IsDouble(standIn) != DominoTileId.IsDouble(tileId))
            standIn = DominoTileId.From(required, (required + 2) % (DominoTileId.MaxPips + 1));

        if (!TryPreviewPlacement(plays, spec, standIn, end, out var slot))
            return false;

        DominoTileId.Split(tileId, out var low, out var high);
        placement = new TilePlacement(tileId, low, high, slot.Center, slot.Yaw, slot.HalfExtents,
            DominoTileId.IsDouble(tileId), end, isOpening: false);

        return true;
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
