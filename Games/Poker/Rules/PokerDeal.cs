using System.Collections.Generic;

namespace Poker.Rules;

public enum PokerStreet
{
    Preflop = 0,
    Flop = 1,
    Turn = 2,
    River = 3,
    Showdown = 4,
}

/// <summary>Private cards and the deck they came off, produced by one shuffle.</summary>
public sealed class HoldemDeal
{
    public readonly Dictionary<string, List<int>> HoleCards = new();

    /// <summary>What is left, in order. The board is dealt off the front as the streets open.</summary>
    public readonly List<int> Stub = new();
}

/// <summary>
/// Shuffling and dealing Hold'em, driven by an explicit seed so a hand is reproducible in tests.
///
/// The seed is a SERVER SECRET and must never be broadcast: it reconstructs every hole card exactly.
/// Only the two cards a player was dealt travel, and only to their own owner.
/// </summary>
public static class PokerDeal
{
    public const int HoleCardCount = 2;
    public const int BoardCount = 5;

    /// <summary>
    /// How many community cards are face up on a given street. The board only ever grows, so a peer
    /// that missed a message catches up rather than having to unwind anything.
    /// </summary>
    public static int BoardSize(PokerStreet street) =>
        street switch
        {
            PokerStreet.Preflop => 0,
            PokerStreet.Flop => 3,
            PokerStreet.Turn => 4,
            _ => BoardCount,
        };

    public static PokerStreet NextStreet(PokerStreet street) =>
        street >= PokerStreet.Showdown ? PokerStreet.Showdown : street + 1;

    /// <summary>
    /// Two cards each, then the rest kept as the stub.
    ///
    /// Cards go round one at a time rather than two at a time, in the supplied dealing order. The
    /// session passes seats rotated to start left of the button, so a hand dealt here and a hand dealt
    /// at a table from the same deck order agree — including the heads-up button receiving last.
    /// </summary>
    public static HoldemDeal Deal(IReadOnlyList<string> playerIds, ulong seed)
    {
        var result = new HoldemDeal();
        if (playerIds == null || playerIds.Count == 0)
            return result;

        var deck = CardId.FullDeck();
        SeededShuffle.Shuffle(deck, seed);

        foreach (var playerId in playerIds)
            result.HoleCards[playerId] = new List<int>(HoleCardCount);

        var next = 0;
        for (var round = 0; round < HoleCardCount; round++)
        {
            foreach (var playerId in playerIds)
            {
                if (next >= deck.Length)
                    break;

                result.HoleCards[playerId].Add(deck[next++]);
            }
        }

        while (next < deck.Length)
            result.Stub.Add(deck[next++]);

        return result;
    }

    /// <summary>
    /// The whole board for a hand, taken off the stub with a card burned before each street exactly
    /// as at a table. Dealt up front because the board is a pure function of the shuffle: the server
    /// simply stops revealing it early, and no card is chosen after anyone has seen a bet.
    /// </summary>
    public static List<int> DealBoard(IReadOnlyList<int> stub)
    {
        var board = new List<int>(BoardCount);
        if (stub == null)
            return board;

        var next = 0;

        // Burn, then three for the flop; burn, then one; burn, then one.
        foreach (var take in new[] { 3, 1, 1 })
        {
            next++;

            for (var i = 0; i < take && next < stub.Count; i++)
                board.Add(stub[next++]);
        }

        return board;
    }
}
