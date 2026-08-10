using System;

namespace Domino.Rules;

/// <summary>
/// Identity for the 28 tiles of a double-six set, as a canonical id in 0..27 for the unordered
/// pair (a,b) with a &lt;= b.
///
/// A tile is an id and nothing else, so a hand, the boneyard and the played chain are all just
/// int arrays — which cross the network as PackedInt32Array with no conversion, the same way
/// Pool.Simulation's ShotInput keeps the wire format down to a handful of numbers.
/// </summary>
public static class DominoTileId
{
    public const int MaxPips = 6;

    /// <summary>Number of tiles in a double-six set.</summary>
    public const int Count = 28;

    /// <summary>Value of an end of the chain before any tile has been played.</summary>
    public const int NoEnd = -1;

    private static readonly int[] LowHalves = new int[Count];
    private static readonly int[] HighHalves = new int[Count];

    static DominoTileId()
    {
        for (var low = 0; low <= MaxPips; low++)
        {
            for (var high = low; high <= MaxPips; high++)
            {
                var id = From(low, high);
                LowHalves[id] = low;
                HighHalves[id] = high;
            }
        }
    }

    /// <summary>
    /// Canonical id for a pair of halves, in either order. The formula walks the triangular
    /// numbering: each row `low` starts after the rows above it have been laid out.
    /// </summary>
    public static int From(int a, int b)
    {
        if (a is < 0 or > MaxPips || b is < 0 or > MaxPips)
            throw new ArgumentOutOfRangeException($"Metades fora de 0..{MaxPips}: ({a},{b}).");

        var low = Math.Min(a, b);
        var high = Math.Max(a, b);

        return low * (MaxPips + 1) - low * (low - 1) / 2 + (high - low);
    }

    public static bool IsValid(int id) => id is >= 0 and < Count;

    public static int Low(int id) => LowHalves[id];

    public static int High(int id) => HighHalves[id];

    public static void Split(int id, out int low, out int high)
    {
        low = LowHalves[id];
        high = HighHalves[id];
    }

    public static int Pips(int id) => LowHalves[id] + HighHalves[id];

    public static bool IsDouble(int id) => LowHalves[id] == HighHalves[id];

    /// <summary>Whether the tile carries a half worth <paramref name="pips"/>.</summary>
    public static bool Matches(int id, int pips) => LowHalves[id] == pips || HighHalves[id] == pips;

    /// <summary>
    /// The half left over once <paramref name="matchedPips"/> has been laid against the chain —
    /// the value the end takes on after this tile is played. Returns <see cref="NoEnd"/> when the
    /// tile does not carry that half at all.
    ///
    /// This is why a play needs no "flipped" flag: which half meets the chain is decided by the
    /// end being played on, never by the client.
    /// </summary>
    public static int OtherHalf(int id, int matchedPips)
    {
        if (LowHalves[id] == matchedPips)
            return HighHalves[id];

        if (HighHalves[id] == matchedPips)
            return LowHalves[id];

        return NoEnd;
    }

    public static string Label(int id) =>
        IsValid(id) ? $"{LowHalves[id]}|{HighHalves[id]}" : "??";

    /// <summary>A fresh, ordered double-six set.</summary>
    public static int[] FullSet()
    {
        var set = new int[Count];
        for (var i = 0; i < Count; i++)
            set[i] = i;

        return set;
    }
}
