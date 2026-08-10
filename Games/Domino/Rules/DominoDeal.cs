using System.Collections.Generic;

namespace Domino.Rules;

/// <summary>Hands and boneyard produced by one shuffle.</summary>
public sealed class DealResult
{
    public readonly Dictionary<string, List<int>> Hands = new();
    public readonly List<int> Boneyard = new();
}

/// <summary>
/// Shuffling and dealing, driven by an explicit seed so a deal is reproducible in tests.
///
/// The seed is a SERVER SECRET and must never be broadcast: it reconstructs every hand exactly.
/// Only the dealt hands travel, and only to their own owner.
/// </summary>
public static class DominoDeal
{
    /// <summary>
    /// Six tiles at a full table, seven otherwise. Four hands of seven would consume the whole set
    /// and leave no boneyard, which removes drawing from the game entirely.
    /// </summary>
    public static int HandSize(int playerCount, int handSizeOverride = 0)
    {
        var standard = playerCount >= 4 ? 6 : 7;
        return handSizeOverride > 0
            && playerCount > 0
            && handSizeOverride * playerCount <= DominoTileId.Count
            ? handSizeOverride
            : standard;
    }

    public static DealResult Deal(IReadOnlyList<string> playerIds, ulong seed, int handSizeOverride = 0)
    {
        var result = new DealResult();
        var deck = DominoTileId.FullSet();
        SeededShuffle.Shuffle(deck, seed);

        var handSize = HandSize(playerIds.Count, handSizeOverride);
        var next = 0;

        foreach (var playerId in playerIds)
        {
            var hand = new List<int>(handSize);
            for (var i = 0; i < handSize && next < deck.Length; i++)
                hand.Add(deck[next++]);

            result.Hands[playerId] = hand;
        }

        while (next < deck.Length)
            result.Boneyard.Add(deck[next++]);

        return result;
    }

    /// <summary>
    /// Who leads and with what: the highest double on the table, or the heaviest tile when nobody
    /// holds a double. Ties fall to the earliest seat in the turn order, so the opening is a pure
    /// function of the deal rather than of dictionary iteration order.
    /// </summary>
    public static string PickOpener(
        IReadOnlyDictionary<string, List<int>> hands,
        IReadOnlyList<string> turnOrder,
        out int openingTileId)
    {
        string bestPlayer = null;
        var bestTile = DominoTileId.NoEnd;
        var bestIsDouble = false;

        foreach (var playerId in turnOrder)
        {
            if (!hands.TryGetValue(playerId, out var hand))
                continue;

            foreach (var tileId in hand)
            {
                if (!DominoTileId.IsValid(tileId))
                    continue;

                var isDouble = DominoTileId.IsDouble(tileId);

                if (bestPlayer == null || Outranks(tileId, isDouble, bestTile, bestIsDouble))
                {
                    bestPlayer = playerId;
                    bestTile = tileId;
                    bestIsDouble = isDouble;
                }
            }
        }

        openingTileId = bestTile;
        return bestPlayer;
    }

    /// <summary>
    /// A double always beats a non-double; within a class the heavier tile wins, and among equal
    /// weights the one with the larger half (5|1 over 4|2).
    /// </summary>
    private static bool Outranks(int tileId, bool isDouble, int againstId, bool againstIsDouble)
    {
        if (isDouble != againstIsDouble)
            return isDouble;

        var pips = DominoTileId.Pips(tileId);
        var againstPips = DominoTileId.Pips(againstId);
        if (pips != againstPips)
            return pips > againstPips;

        return DominoTileId.High(tileId) > DominoTileId.High(againstId);
    }
}
