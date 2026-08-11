using System.Collections.Generic;
using Godot;

/// <summary>
/// Deterministic resting places for chips that a player has put forward but has not yet swept into
/// the pot. The horizontal candidates deliberately overlap: a real handful occupies a few short,
/// imperfect stacks rather than a ring of evenly spaced counters. Whenever two projected discs
/// overlap, the later chip is raised onto the highest one below it, so their volumes never intersect.
/// </summary>
public static class PokerChipContactLayout
{
    public const int ChipsPerCluster = 5;
    public const float HorizontalClearance = 0.0002f;
    public const float VerticalClearance = 0.0001f;
    public const float MaximumDrift = 0.0015f;

    /// <summary>
    /// Returns the root offset for a one-chip <see cref="PokerChipPile"/>. The visual in that pile is
    /// already centred at half its thickness, therefore Y is the bottom of the chip, not its centre.
    /// Slot zero intentionally matches the centre used by an initial blind.
    /// </summary>
    public static Vector3 RootOffset(int slot, float diameter, float thickness)
    {
        slot = Mathf.Max(0, slot);
        diameter = Mathf.Max(0.001f, diameter);
        thickness = Mathf.Max(0.0001f, thickness);

        var placed = BuildThrough(slot, diameter, thickness);
        var result = placed[slot];
        return new Vector3(result.Horizontal.X, result.Bottom, result.Horizontal.Y);
    }

    private readonly struct RestingChip
    {
        public readonly Vector2 Horizontal;
        public readonly float Bottom;

        public RestingChip(Vector2 horizontal, float bottom)
        {
            Horizontal = horizontal;
            Bottom = bottom;
        }
    }

    private static List<RestingChip> BuildThrough(int lastSlot, float diameter, float thickness)
    {
        var placed = new List<RestingChip>(lastSlot + 1);
        for (var slot = 0; slot <= lastSlot; slot++)
        {
            var candidate = HorizontalCandidate(slot, diameter);
            var bottom = 0.0f;

            // Flat cylinders whose projections overlap cannot share a vertical interval. Placing the
            // new bottom above the highest overlapping top is a small deterministic contact solver.
            foreach (var support in placed)
            {
                if (candidate.DistanceTo(support.Horizontal) >= diameter + HorizontalClearance)
                    continue;
                bottom = Mathf.Max(bottom, support.Bottom + thickness + VerticalClearance);
            }

            placed.Add(new RestingChip(candidate, bottom));
        }
        return placed;
    }

    private static Vector2 HorizontalCandidate(int slot, float diameter)
    {
        var column = slot / ChipsPerCluster;
        // Neighbouring short stacks may touch, but their worst-case opposing drifts still cannot put
        // two chips from the same layer through each other.
        var columnSpacing = diameter + MaximumDrift * 2.0f + HorizontalClearance;
        var basePlace = PokerChipPile.HexSlot(column, columnSpacing);
        if (slot == 0)
            return basePlace;

        // The drift is small enough to keep a column visibly supported, but large enough that its
        // silhouettes do not read as one computer-perfect cylinder. Noise is a stable integer hash.
        var drift = new Vector2(
            PokerChipPile.Noise(slot, 71),
            PokerChipPile.Noise(slot, 72));
        if (drift.LengthSquared() > 1.0f)
            drift = drift.Normalized();
        return basePlace + drift * MaximumDrift;
    }
}
