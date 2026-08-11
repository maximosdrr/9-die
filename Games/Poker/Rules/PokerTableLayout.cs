using Godot;

namespace Poker.Rules;

/// <summary>Where everything sits on the cloth, in metres, table-local.</summary>
public readonly struct PokerLayoutSpec
{
    /// <summary>Across a card's face (table-local X when the card lies in the board row).</summary>
    public readonly float CardWidth;

    /// <summary>Along a card's face (table-local Z).</summary>
    public readonly float CardLength;

    public readonly float CardThickness;

    /// <summary>Space between neighbouring community cards.</summary>
    public readonly float CardGap;

    /// <summary>How far from the middle the community row sits, toward the far side.</summary>
    public readonly float BoardOffset;

    /// <summary>Where the pot's chips pile up, between the board and the near edge.</summary>
    public readonly float PotRadius;

    /// <summary>How far out a seat's two face-down cards lie.</summary>
    public readonly float SeatCardRadius;

    /// <summary>How far out a seat's current bet is pushed.</summary>
    public readonly float SeatBetRadius;

    /// <summary>How far out a seat's own stack rests.</summary>
    public readonly float SeatStackRadius;

    public PokerLayoutSpec(
        float cardWidth, float cardLength, float cardThickness, float cardGap,
        float boardOffset, float potRadius,
        float seatCardRadius, float seatBetRadius, float seatStackRadius)
    {
        CardWidth = cardWidth;
        CardLength = cardLength;
        CardThickness = cardThickness;
        CardGap = cardGap;
        BoardOffset = boardOffset;
        PotRadius = potRadius;
        SeatCardRadius = seatCardRadius;
        SeatBetRadius = seatBetRadius;
        SeatStackRadius = seatStackRadius;
    }

    /// <summary>
    /// Tuned for the same 0.64 m bar table the dominoes use, and for the same reason: a card big
    /// enough to read from a chair, on a table small enough that the chair is close to it. A real
    /// card is 63 x 88 mm; these are 76 x 106, large enough to read from the chair while still
    /// leaving the five-card row clear of every seat's own cards.
    /// </summary>
    /// <summary>
    /// The radii read outward from the middle in the order a real table does: the bet is pushed
    /// toward the pot, the shown cards sit in front of the player, and their own stack rests
    /// nearest to them. Spread apart deliberately — the first pass had the bet at 0.30 and the
    /// cards at 0.44, close enough that the chips landed on top of the cards.
    /// </summary>
    public static PokerLayoutSpec Default =>
        new(0.076f, 0.106f, 0.0006f, 0.020f,
            boardOffset: 0.0f, potRadius: 0.16f,
            seatCardRadius: 0.40f, seatBetRadius: 0.32f, seatStackRadius: 0.54f);

    /// <summary>Width of the whole five-card row.</summary>
    public float BoardWidth =>
        PokerDeal.BoardCount * CardWidth + (PokerDeal.BoardCount - 1) * CardGap;
}

/// <summary>
/// Where the community cards, the pot and each seat's things belong.
///
/// A pure function of the situation, exactly like the domino chain: every peer computes the same
/// table from the same public state, so NO POSITION EVER TRAVELS OVER THE NETWORK. A card reaching
/// the board costs one int, not a node spawn and a transform.
/// </summary>
public static class PokerTableLayout
{
    /// <summary>
    /// Table-local centre of community card <paramref name="index"/>, 0..4. Fixed by index rather
    /// than by how many are face up, so turning the turn and the river never slides the flop.
    /// </summary>
    public static Vector2 BoardPosition(int index, PokerLayoutSpec spec)
    {
        var step = spec.CardWidth + spec.CardGap;
        var x = (index - (PokerDeal.BoardCount - 1) * 0.5f) * step;

        return new Vector2(x, spec.BoardOffset);
    }

    /// <summary>
    /// Where a seat's things sit, given the direction from the middle of the table out to that
    /// chair. Everything a seat owns is on its own radial line, so a player reads their own row
    /// straight out in front of them however the table is turned.
    /// </summary>
    public static Vector2 SeatSpot(Vector2 facing, float radius) => facing.Normalized() * radius;

    /// <summary>
    /// The two face-down cards in front of a seat, side by side and square to that seat.
    /// <paramref name="index"/> is 0 or 1.
    /// </summary>
    public static Vector2 SeatCardPosition(Vector2 facing, int index, PokerLayoutSpec spec)
    {
        var direction = facing.Normalized();
        var across = new Vector2(-direction.Y, direction.X);
        var step = spec.CardWidth * 0.62f;

        return direction * spec.SeatCardRadius
               + across * ((index - (PokerDeal.HoleCardCount - 1) * 0.5f) * step);
    }

    /// <summary>The yaw that turns a card to face a seat looking in along <paramref name="facing"/>.</summary>
    public static float YawTowardCentre(Vector2 facing) => Mathf.Atan2(facing.X, facing.Y);

    /// <summary>
    /// Furthest a seat's own things reach from the middle — used to check they clear the community
    /// row and still fit on the table.
    /// </summary>
    public static float SeatReach(PokerLayoutSpec spec) =>
        Mathf.Max(spec.SeatStackRadius, spec.SeatCardRadius) + spec.CardLength * 0.5f;

    /// <summary>Half the diagonal of the community row: what it needs clear around the middle.</summary>
    public static float BoardReach(PokerLayoutSpec spec) =>
        new Vector2(spec.BoardWidth * 0.5f, Mathf.Abs(spec.BoardOffset) + spec.CardLength * 0.5f)
            .Length();
}
