/// <summary>
/// Fisher-Yates over splitmix64.
///
/// A self-contained generator rather than GD.Randi or System.Random keeps "same seed, same deal"
/// true across runs, runtimes and machines — which is what lets a deal be replayed in a test and
/// what lets the server replay and audit a deal internally. Determinism alone is not a
/// cryptographic fairness proof; that would require a separate commit/reveal protocol.
///
/// Whatever seeds this is a SERVER SECRET wherever the shuffle decides hidden information: it
/// reconstructs every hand exactly. It must never be broadcast or logged.
/// </summary>
public static class SeededShuffle
{
    public static void Shuffle(int[] deck, ulong seed)
    {
        if (deck == null)
            return;

        var state = seed;

        for (var i = deck.Length - 1; i > 0; i--)
        {
            var j = NextIndex(ref state, i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }

    /// <summary>
    /// Maps the full 64-bit stream to a smaller range without modulo bias. Values below the
    /// threshold are the incomplete tail of 2^64 divided by the bound; rejecting them gives every
    /// possible index exactly the same number of source values.
    /// </summary>
    private static int NextIndex(ref ulong state, int exclusiveUpperBound)
    {
        var bound = (ulong)exclusiveUpperBound;
        var rejectionThreshold = unchecked(0UL - bound) % bound;

        ulong sample;
        do
        {
            sample = Next(ref state);
        }
        while (sample < rejectionThreshold);

        return (int)(sample % bound);
    }

    /// <summary>
    /// One draw from splitmix64, advancing the caller's state. Exposed because a dealer sometimes
    /// needs a further decision from the same stream (which seat gets an odd chip, say) and that
    /// decision has to be as reproducible as the shuffle itself.
    /// </summary>
    public static ulong Next(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
