namespace Poker.Rules;

/// <summary>
/// A playing card as a single int, 0..51.
///
/// One int per card is what lets a hand cross the wire as a PackedInt32Array and lets the whole
/// rules layer work in value types — the same reason a domino tile is an int.
///
/// Rank is the LOGICAL order the game ranks by: 0 is a Two and 12 is an Ace. That is deliberately
/// not the order the art pack lays its columns out in; translating between the two belongs to the
/// presentation layer and nowhere else, so a differently-ordered pack can never change who wins.
/// </summary>
public static class CardId
{
    public const int Count = 52;
    public const int Ranks = 13;
    public const int Suits = 4;

    /// <summary>No card.</summary>
    public const int None = -1;

    // Ranks, in the order they beat each other.
    public const int Two = 0;
    public const int Five = 3;
    public const int Ten = 8;
    public const int Jack = 9;
    public const int Queen = 10;
    public const int King = 11;
    public const int Ace = 12;

    // Suits. No suit beats another anywhere in Hold'em; this order matches the art pack's rows only
    // so the presentation table stays a straight index.
    public const int Clubs = 0;
    public const int Diamonds = 1;
    public const int Hearts = 2;
    public const int Spades = 3;

    private static readonly string[] RankLabels =
        { "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A" };

    private static readonly string[] SuitLabels = { "♣", "♦", "♥", "♠" };

    public static bool IsValid(int cardId) => cardId >= 0 && cardId < Count;

    public static int From(int rank, int suit) => suit * Ranks + rank;

    public static int RankOf(int cardId) => cardId % Ranks;

    public static int SuitOf(int cardId) => cardId / Ranks;

    public static string Label(int cardId) =>
        IsValid(cardId)
            ? RankLabels[RankOf(cardId)] + SuitLabels[SuitOf(cardId)]
            : "??";

    /// <summary>All 52, in id order. The caller shuffles.</summary>
    public static int[] FullDeck()
    {
        var deck = new int[Count];
        for (var i = 0; i < Count; i++)
            deck[i] = i;

        return deck;
    }
}
