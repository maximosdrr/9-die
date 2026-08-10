using System.Collections.Generic;
using Godot;

namespace Domino.Rules;

/// <summary>Physical dimensions the chain is laid out against, in metres, table-local.</summary>
public readonly struct LayoutSpec
{
    /// <summary>Long axis of a tile.</summary>
    public readonly float TileLength;

    /// <summary>Short axis of a tile.</summary>
    public readonly float TileWidth;

    public readonly float TileThickness;

    /// <summary>Breathing room left between neighbouring tiles.</summary>
    public readonly float Gap;

    /// <summary>Half extents of the usable area on the cloth, X and Z.</summary>
    public readonly Vector2 PlayHalfExtents;

    public LayoutSpec(float tileLength, float tileWidth, float tileThickness, float gap, Vector2 playHalfExtents)
    {
        TileLength = tileLength;
        TileWidth = tileWidth;
        TileThickness = tileThickness;
        Gap = gap;
        PlayHalfExtents = playHalfExtents;
    }

    /// <summary>
    /// A 72 x 36 x 11.6 mm tile on a 0.84 x 0.64 m playing area. The dimensions are the art pack's
    /// own, scaled to metres (see DominoTileMeshes), so the footprint the chain reserves matches
    /// the model that lands in it.
    ///
    /// Oversized against a real domino on purpose, for legibility from the seat. Sized so a full
    /// 28-tile chain still fits with two corners per branch and room to spare; the layout test is
    /// what proves that, and is the gate on ever changing these numbers.
    /// </summary>
    public static LayoutSpec Default =>
        new(0.072f, 0.036f, 0.0116f, 0.002f, new Vector2(0.42f, 0.32f));
}

/// <summary>Where one tile ends up and which way round it faces.</summary>
public readonly struct TilePlacement
{
    public readonly int TileId;

    /// <summary>Pip facing back down the chain, toward the tile before it.</summary>
    public readonly int Incoming;

    /// <summary>Pip facing forward, which becomes the branch's open end.</summary>
    public readonly int Outgoing;

    /// <summary>Table-local position on the cloth: X is X, Y is Z.</summary>
    public readonly Vector2 Center;

    /// <summary>Rotation about +Y. Always an exact multiple of a quarter turn.</summary>
    public readonly float Yaw;

    /// <summary>Axis-aligned half extents of the footprint, since every tile lies on an axis.</summary>
    public readonly Vector2 HalfExtents;

    public readonly bool IsDouble;

    /// <summary>Which branch this tile grew, meaningless for the opening tile.</summary>
    public readonly ChainEnd Branch;

    public readonly bool IsOpening;

    public TilePlacement(int tileId, int incoming, int outgoing, Vector2 center, float yaw,
        Vector2 halfExtents, bool isDouble, ChainEnd branch, bool isOpening)
    {
        TileId = tileId;
        Incoming = incoming;
        Outgoing = outgoing;
        Center = center;
        Yaw = yaw;
        HalfExtents = halfExtents;
        IsDouble = isDouble;
        Branch = branch;
        IsOpening = isOpening;
    }
}

public sealed class ChainLayout
{
    public readonly List<TilePlacement> Placements = new();

    /// <summary>Set when a tile had nowhere legal to go and was stacked on anyway.</summary>
    public bool OverflowedTable;

    /// <summary>Corners turned by the busiest branch. The layout test keeps this small.</summary>
    public int MaxTurnsPerBranch;
}

/// <summary>
/// Turns the ordered list of plays into positions and rotations on the cloth.
///
/// Deterministic and pure: the same plays and the same spec always produce the same transforms, so
/// positions are never sent over the network — each peer derives them, exactly as
/// PoolSimulationRunner ships a ShotInput instead of a per-frame position stream. Every direction
/// is axis-aligned and every rotation is an exact quarter turn, so there is no trigonometry to
/// drift between machines.
///
/// The chain grows from the middle in two branches. Both turn the same way when they run out of
/// room, which puts them 180 degrees out of phase — the right branch hugs one half of the table
/// and the left branch the other, so they never meet.
/// </summary>
public static class DominoChainLayout
{
    private static readonly Vector2I Right = new(1, 0);
    private static readonly Vector2I Forward = new(0, 1);

    private struct BranchCursor
    {
        /// <summary>Far edge of the last tile, measured along <see cref="Direction"/>.</summary>
        public Vector2 Position;
        public Vector2I Direction;
        public int Turns;
    }

    public static ChainLayout Rebuild(IReadOnlyList<PlayRecord> plays, LayoutSpec spec)
    {
        var layout = new ChainLayout();
        if (plays == null || plays.Count == 0)
            return layout;

        var board = new DominoBoardState();
        var leftCursor = default(BranchCursor);
        var rightCursor = default(BranchCursor);

        foreach (var play in plays)
        {
            if (board.IsEmpty)
            {
                layout.Placements.Add(PlaceOpening(play.TileId, spec, ref leftCursor, ref rightCursor));
                board.TryPlay(play.PlayerId, play.TileId, play.End);
                continue;
            }

            var incoming = board.ValueAt(play.End);
            var outgoing = DominoTileId.OtherHalf(play.TileId, incoming);
            if (outgoing == DominoTileId.NoEnd)
            {
                // The play list disagrees with itself. Stop rather than lay tiles that lie about
                // which pips they are showing.
                layout.OverflowedTable = true;
                break;
            }

            ref var cursor = ref (play.End == ChainEnd.Left ? ref leftCursor : ref rightCursor);
            var isDouble = DominoTileId.IsDouble(play.TileId);

            var fitted = Advance(ref cursor, spec, isDouble, out var center, out var direction,
                out var halfExtents);
            if (!fitted)
                layout.OverflowedTable = true;

            layout.Placements.Add(new TilePlacement(
                play.TileId, incoming, outgoing, center,
                YawFor(play.TileId, isDouble, outgoing, direction),
                halfExtents, isDouble, play.End, isOpening: false));

            board.TryPlay(play.PlayerId, play.TileId, play.End);
        }

        layout.MaxTurnsPerBranch = Mathf.Max(leftCursor.Turns, rightCursor.Turns);
        return layout;
    }

    /// <summary>
    /// The first tile sits at the centre with its long axis across the table, low half toward the
    /// left branch and high half toward the right — which is exactly the split
    /// <see cref="DominoBoardState.TryPlay"/> makes for the opening ends.
    /// </summary>
    private static TilePlacement PlaceOpening(int tileId, LayoutSpec spec,
        ref BranchCursor leftCursor, ref BranchCursor rightCursor)
    {
        var isDouble = DominoTileId.IsDouble(tileId);
        var along = isDouble ? spec.TileWidth : spec.TileLength;
        var across = isDouble ? spec.TileLength : spec.TileWidth;
        var halfExtents = new Vector2(along * 0.5f, across * 0.5f);

        DominoTileId.Split(tileId, out var low, out var high);

        // A double lies crosswise, so its long axis runs perpendicular to the branch axis.
        var yaw = isDouble ? YawFor(RotateCcw(Right)) : YawFor(Right);

        rightCursor = new BranchCursor { Position = new Vector2(along * 0.5f, 0.0f), Direction = Right };
        leftCursor = new BranchCursor { Position = new Vector2(-along * 0.5f, 0.0f), Direction = -Right };

        return new TilePlacement(tileId, low, high, Vector2.Zero, yaw, halfExtents, isDouble,
            ChainEnd.Right, isOpening: true);
    }

    /// <summary>
    /// Places the next tile on a branch, turning the corner when going straight would run off the
    /// cloth. Returns false when even a corner has nowhere to go, in which case the tile is stacked
    /// straight ahead anyway — a chain that pokes off the table is far better than one that stops
    /// matching what the other peers drew.
    /// </summary>
    private static bool Advance(ref BranchCursor cursor, LayoutSpec spec, bool isDouble,
        out Vector2 center, out Vector2I direction, out Vector2 halfExtents)
    {
        var entry = cursor.Direction;
        var entryVector = ToVector(entry);
        var along = isDouble ? spec.TileWidth : spec.TileLength;
        var across = isDouble ? spec.TileLength : spec.TileWidth;

        // A corner tile lies across the branch's old direction, so it eats `across` of room rather
        // than `along`. The worst case is a double turning the corner, which needs a full tile
        // length ahead — reserve exactly that before committing to another straight tile.
        var cornerReserve = spec.TileLength + spec.Gap;
        if (isDouble)
        {
            // A double advances only by its width. If it uses the ordinary reserve near a rail,
            // the following normal tile is forced to turn immediately and ends up parallel beside
            // the crosswise double. Reserve one guaranteed normal follower as well; when that room
            // is unavailable, the double itself becomes the corner and its follower stays clear.
            cornerReserve += spec.TileLength + spec.Gap;
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var candidateDirection = entry;
            for (var turn = 0; turn < attempt; turn++)
                candidateDirection = RotateCcw(candidateDirection);

            var candidateExtents = HalfExtentsFor(candidateDirection, along, across);
            var candidateCenter = attempt == 0
                ? cursor.Position + entryVector * (along * 0.5f + spec.Gap)
                : cursor.Position + entryVector * (across * 0.5f + spec.Gap);

            if (!Inside(candidateCenter, candidateExtents, spec))
                continue;

            var nextPosition = candidateCenter + ToVector(candidateDirection) * (along * 0.5f);

            // Going straight also has to leave room to turn afterwards, otherwise the branch runs
            // its nose into the rail and the corner tile is the one that overhangs.
            if (attempt == 0 && Clearance(nextPosition, candidateDirection, spec) < cornerReserve)
                continue;

            cursor.Direction = candidateDirection;
            cursor.Position = nextPosition;
            cursor.Turns += attempt;

            center = candidateCenter;
            direction = candidateDirection;
            halfExtents = candidateExtents;
            return true;
        }

        direction = entry;
        halfExtents = HalfExtentsFor(entry, along, across);
        center = cursor.Position + entryVector * (along * 0.5f + spec.Gap);
        cursor.Position = center + entryVector * (along * 0.5f);
        return false;
    }

    private static bool Inside(Vector2 center, Vector2 halfExtents, LayoutSpec spec) =>
        Mathf.Abs(center.X) + halfExtents.X <= spec.PlayHalfExtents.X
        && Mathf.Abs(center.Y) + halfExtents.Y <= spec.PlayHalfExtents.Y;

    /// <summary>How much cloth is left ahead of a point in a direction.</summary>
    private static float Clearance(Vector2 position, Vector2I direction, LayoutSpec spec)
    {
        var wall = direction.X != 0 ? spec.PlayHalfExtents.X : spec.PlayHalfExtents.Y;
        var travelled = position.X * direction.X + position.Y * direction.Y;
        return wall - travelled;
    }

    private static Vector2 HalfExtentsFor(Vector2I direction, float along, float across) =>
        direction.X != 0
            ? new Vector2(along * 0.5f, across * 0.5f)
            : new Vector2(across * 0.5f, along * 0.5f);

    private static Vector2I RotateCcw(Vector2I direction) => new(-direction.Y, direction.X);

    private static Vector2 ToVector(Vector2I direction) => new(direction.X, direction.Y);

    /// <summary>
    /// Rotation that points the tile's high half — its local +Z, by the authoring convention of
    /// DominoTile.tscn — the right way. A double lies crosswise instead, and since both its halves
    /// are equal there is nothing to get backwards.
    /// </summary>
    private static float YawFor(int tileId, bool isDouble, int outgoing, Vector2I direction)
    {
        if (isDouble)
            return YawFor(RotateCcw(direction));

        return outgoing == DominoTileId.High(tileId)
            ? YawFor(direction)
            : YawFor(-direction);
    }

    /// <summary>
    /// Rotation about +Y that maps a node's local +Z onto an axis direction. Written as exact
    /// constants rather than Atan2 so two peers cannot disagree by an ulp.
    /// </summary>
    private static float YawFor(Vector2I direction)
    {
        if (direction == Forward)
            return 0.0f;

        if (direction == Right)
            return Mathf.Pi * 0.5f;

        if (direction == -Forward)
            return Mathf.Pi;

        return -Mathf.Pi * 0.5f;
    }
}
