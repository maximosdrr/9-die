/// <summary>
/// Fisher-Yates over splitmix64.
///
/// A self-contained generator rather than GD.Randi or System.Random keeps "same seed, same deal"
/// true across runs, runtimes and machines — which is what lets a deal be replayed in a test and
/// what lets the server hand out a shuffle it can prove it did not rig after the fact.
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
            var j = (int)(Next(ref state) % (ulong)(i + 1));
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
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
