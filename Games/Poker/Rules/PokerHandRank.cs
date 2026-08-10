using System;

namespace Poker.Rules;

public enum HandCategory
{
    HighCard = 0,
    Pair = 1,
    TwoPair = 2,
    ThreeOfAKind = 3,
    Straight = 4,
    Flush = 5,
    FullHouse = 6,
    FourOfAKind = 7,
    StraightFlush = 8,
}

/// <summary>
/// How good a five-card hand is, as ONE comparable integer.
///
/// Category and up to five tiebreak ranks are packed into a single value, so comparing two hands is
/// an int comparison rather than a cascade of special cases. That matters more than it looks: a
/// showdown has to answer "who wins" AND "is this an exact tie" — a split pot is decided by two
/// hands comparing EQUAL, so any comparison that is merely "good enough to order them" would
/// quietly hand one player the whole pot.
///
/// Suits are never part of the value. No suit beats another in Hold'em, and a flush of the same
/// ranks in different suits is a genuine tie.
/// </summary>
public readonly struct PokerHandRank : IComparable<PokerHandRank>, IEquatable<PokerHandRank>
{
    /// <summary>Four bits per tiebreak rank, five of them, under the category.</summary>
    private const int CategoryShift = 20;

    public readonly int Value;

    public PokerHandRank(int value) => Value = value;

    public PokerHandRank(HandCategory category, int r1 = 0, int r2 = 0, int r3 = 0, int r4 = 0, int r5 = 0)
    {
        Value = ((int)category << CategoryShift)
                | (r1 << 16) | (r2 << 12) | (r3 << 8) | (r4 << 4) | r5;
    }

    public HandCategory Category => (HandCategory)(Value >> CategoryShift);

    /// <summary>Nothing — used for a player who folded and never reached a showdown.</summary>
    public static PokerHandRank None => new(-1);

    public int CompareTo(PokerHandRank other) => Value.CompareTo(other.Value);

    public bool Equals(PokerHandRank other) => Value == other.Value;

    public override bool Equals(object obj) => obj is PokerHandRank other && Equals(other);

    public override int GetHashCode() => Value;

    public static bool operator >(PokerHandRank a, PokerHandRank b) => a.Value > b.Value;
    public static bool operator <(PokerHandRank a, PokerHandRank b) => a.Value < b.Value;
    public static bool operator >=(PokerHandRank a, PokerHandRank b) => a.Value >= b.Value;
    public static bool operator <=(PokerHandRank a, PokerHandRank b) => a.Value <= b.Value;
    public static bool operator ==(PokerHandRank a, PokerHandRank b) => a.Value == b.Value;
    public static bool operator !=(PokerHandRank a, PokerHandRank b) => a.Value != b.Value;

    private static readonly string[] CategoryNames =
    {
        "Carta alta", "Um par", "Dois pares", "Trinca", "Sequência",
        "Flush", "Full house", "Quadra", "Straight flush",
    };

    public string Describe() =>
        Value < 0
            ? "Sem mão"
            : CategoryNames[(int)Category];
}
