using System;
using System.Collections.Generic;

namespace Pool.Simulation;

/// <summary>
/// A straight cushion, as a horizontal line segment on the cloth plus the inward unit normal.
/// Endpoints are the pocket jaws, so a ball can also strike the segment's END — handled
/// separately by the collision code, which is what makes a ball rattle in the jaws instead of
/// passing through a gap.
/// </summary>
public readonly struct CushionSegment
{
    public readonly Vec3d Start;
    public readonly Vec3d End;
    public readonly Vec3d Normal;

    public CushionSegment(Vec3d start, Vec3d end, Vec3d normal)
    {
        Start = start;
        End = end;
        Normal = normal;
    }
}

public readonly struct Pocket
{
    public readonly Vec3d Center;
    public readonly double Radius;

    public Pocket(Vec3d center, double radius)
    {
        Center = center;
        Radius = radius;
    }
}

/// <summary>
/// The table as NUMBERS rather than collision shapes. This is what replaces the rail geometry for
/// ball physics: the scene's rails stay as visual meshes, but what the balls actually bounce off
/// is defined here, so a mis-rotated mesh can no longer warp the physics.
///
/// Pockets are given individually rather than mirrored from a half-width, because real tables
/// aren't symmetric — the pockets on this project's Table2 vary by about 2 cm in position and
/// have different radii, and forcing symmetry put the physics pockets visibly off the ones the
/// player sees. PoolTableGeometry builds this from each table scene's own nodes.
///
/// Coordinates are table-local, centred on the table with +Y up.
/// </summary>
public sealed class TableSpec
{
    /// <summary>Centre of the playing surface. Nothing here is assumed to be centred on the origin.</summary>
    public readonly Vec3d PlayCentre;

    /// <summary>Half extent of the playing surface along X (the short axis).</summary>
    public readonly double HalfWidth;

    /// <summary>Half extent of the playing surface along Z (the long axis).</summary>
    public readonly double HalfLength;

    public readonly IReadOnlyList<CushionSegment> Cushions;
    public readonly IReadOnlyList<Pocket> Pockets;

    /// <summary>
    /// How far below the cloth a ball must fall before it counts as gone rather than potted.
    /// Used to distinguish a pot from a ball driven off the table.
    /// </summary>
    public readonly double DropDepth;

    /// <summary>
    /// Fully explicit table: every cushion and every pocket is given, positioned in its own right.
    /// Nothing is mirrored or inferred, because table shapes vary and a rule like "cushions sit at
    /// the edge of the cloth" stops holding the moment a table isn't a plain rectangle.
    /// </summary>
    public TableSpec(
        Vec3d playCentre,
        double halfWidth,
        double halfLength,
        IReadOnlyList<CushionSegment> cushions,
        IReadOnlyList<Pocket> pockets,
        double dropDepth = 0.1)
    {
        PlayCentre = playCentre;
        HalfWidth = halfWidth;
        HalfLength = halfLength;
        DropDepth = dropDepth;
        Cushions = cushions ?? Array.Empty<CushionSegment>();
        Pockets = pockets ?? Array.Empty<Pocket>();
    }

    /// <summary>True when the point lies over the playing surface, used for off-table detection.</summary>
    public bool IsOverPlaySurface(Vec3d position, double margin)
    {
        return Math.Abs(position.X - PlayCentre.X) <= HalfWidth + margin
               && Math.Abs(position.Z - PlayCentre.Z) <= HalfLength + margin;
    }

    /// <summary>
    /// Finds the pocket capture circle containing the ball centre. Pocket mouths deliberately
    /// override the rectangular cloth footprint: visually the cloth marker is a box, but these
    /// circles are the holes cut out of that box for simulation purposes.
    /// </summary>
    public bool TryGetPocketAt(Vec3d position, out int pocketIndex)
    {
        for (var i = 0; i < Pockets.Count; i++)
        {
            var pocket = Pockets[i];
            var offset = (position - pocket.Center).Flat;
            if (offset.FlatLengthSquared > pocket.Radius * pocket.Radius)
                continue;

            pocketIndex = i;
            return true;
        }

        pocketIndex = -1;
        return false;
    }

    /// <summary>
    /// True only where the rectangular bed really supports a ball. A point over a pocket is not
    /// supported even though it is still inside the cloth marker's bounding rectangle.
    /// </summary>
    public bool HasClothSupport(Vec3d position)
    {
        return IsOverPlaySurface(position, 0.0)
               && !TryGetPocketAt(position, out _);
    }

    /// <summary>
    /// Lays cushion segments along the four sides, ending each one where the nearest pocket's
    /// mouth begins. Pockets are matched to a side by which one they sit closest to, so a table
    /// whose pockets are slightly off-centre still gets its gaps in the right places.
    /// </summary>
    private static List<CushionSegment> BuildCushions(
        double halfWidth,
        double halfLength,
        IReadOnlyList<Pocket> pockets)
    {
        var cushions = new List<CushionSegment>();

        AddCushionsAlongSide(cushions, pockets, halfLength, halfWidth,
            isLongSide: false, sideSign: 1.0);
        AddCushionsAlongSide(cushions, pockets, halfLength, halfWidth,
            isLongSide: false, sideSign: -1.0);
        AddCushionsAlongSide(cushions, pockets, halfWidth, halfLength,
            isLongSide: true, sideSign: 1.0);
        AddCushionsAlongSide(cushions, pockets, halfWidth, halfLength,
            isLongSide: true, sideSign: -1.0);

        return cushions;
    }

    private static void AddCushionsAlongSide(
        List<CushionSegment> cushions,
        IReadOnlyList<Pocket> pockets,
        double sideOffset,
        double sideExtent,
        bool isLongSide,
        double sideSign)
    {
        // Gaps this side must leave, as [from, to] spans along the side's own axis.
        var gaps = new List<(double From, double To)>();

        foreach (var pocket in pockets)
        {
            var acrossAxis = isLongSide ? pocket.Center.X : pocket.Center.Z;
            var alongAxis = isLongSide ? pocket.Center.Z : pocket.Center.X;

            // Only pockets that belong to this side; the far side's pockets are a table away.
            if (Math.Sign(acrossAxis) != Math.Sign(sideSign) || Math.Abs(acrossAxis) < sideOffset * 0.5)
                continue;

            gaps.Add((alongAxis - pocket.Radius, alongAxis + pocket.Radius));
        }

        gaps.Sort((a, b) => a.From.CompareTo(b.From));

        var normal = isLongSide
            ? new Vec3d(-sideSign, 0.0, 0.0)
            : new Vec3d(0.0, 0.0, -sideSign);

        var cursor = -sideExtent;

        foreach (var gap in gaps)
        {
            if (gap.From > cursor)
                cushions.Add(MakeSegment(cursor, gap.From, sideOffset * sideSign, isLongSide, normal));

            cursor = Math.Max(cursor, gap.To);
        }

        if (cursor < sideExtent)
            cushions.Add(MakeSegment(cursor, sideExtent, sideOffset * sideSign, isLongSide, normal));
    }

    private static CushionSegment MakeSegment(
        double from,
        double to,
        double sidePosition,
        bool isLongSide,
        Vec3d normal)
    {
        var start = isLongSide
            ? new Vec3d(sidePosition, 0.0, from)
            : new Vec3d(from, 0.0, sidePosition);

        var end = isLongSide
            ? new Vec3d(sidePosition, 0.0, to)
            : new Vec3d(to, 0.0, sidePosition);

        return new CushionSegment(start, end, normal);
    }

    /// <summary>
    /// Symmetric regulation-ish table, used as a fallback when a table scene has no markers yet
    /// and by tests that construct a runner standalone. Real tables come from PoolTableGeometry.
    /// </summary>
    public static TableSpec CreateDefault(
        double halfWidth = 0.5088,
        double halfLength = 1.0492,
        double cornerPocketRadius = 0.052,
        double sidePocketRadius = 0.050)
    {
        var pockets = new List<Pocket>
        {
            new(new Vec3d(-halfWidth, 0.0, -halfLength), cornerPocketRadius),
            new(new Vec3d(halfWidth, 0.0, -halfLength), cornerPocketRadius),
            new(new Vec3d(-halfWidth, 0.0, halfLength), cornerPocketRadius),
            new(new Vec3d(halfWidth, 0.0, halfLength), cornerPocketRadius),
            new(new Vec3d(-halfWidth, 0.0, 0.0), sidePocketRadius),
            new(new Vec3d(halfWidth, 0.0, 0.0), sidePocketRadius),
        };

        return new TableSpec(
            Vec3d.Zero,
            halfWidth,
            halfLength,
            BuildCushions(halfWidth, halfLength, pockets),
            pockets);
    }
}
