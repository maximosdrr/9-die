using System.Collections.Generic;

namespace Poker.Rules;

/// <summary>
/// Who posts what and who speaks when, as the dealer button goes round.
///
/// Every rule here has a HEADS-UP exception, and they are the ones that get missed: with two
/// players the button posts the SMALL blind rather than the big one, acts FIRST before the flop and
/// LAST after it — the exact reverse of the three-handed table. Written as one branch per rule so
/// each exception sits next to the rule it bends.
///
/// Seats are indices into whoever is still in the session; as players bust, the list shrinks and
/// these keep working on what is left.
/// </summary>
public static class PokerSeating
{
    public static int Next(int seat, int seatCount) =>
        seatCount <= 0 ? 0 : (seat + 1) % seatCount;

    /// <summary>
    /// Advances the button for the next hand while preserving blind fairness at the three-to-two
    /// transition. The next big blind is the first surviving seat left of the previous big blind;
    /// heads-up, the other survivor is therefore the button/small blind.
    /// </summary>
    public static int NextButtonSeat(
        IReadOnlyList<PlayerBetState> players, int currentButtonSeat)
    {
        if (players == null || players.Count == 0)
            return 0;

        var button = ((currentButtonSeat % players.Count) + players.Count) % players.Count;
        var survivors = 0;
        foreach (var player in players)
        {
            if (player.Stack > 0)
                survivors++;
        }

        if (players.Count <= 2 || survivors != 2)
            return Next(button, players.Count);

        var previousBigBlind = BigBlindSeat(button, players.Count);
        var nextBigBlind = NextSeatWithChips(players, previousBigBlind);
        return NextSeatWithChips(players, nextBigBlind);
    }

    /// <summary>Players receive cards one at a time starting left of the dealer button.</summary>
    public static List<string> DealOrder(
        IReadOnlyList<string> seatedPlayers, int buttonSeat)
    {
        var order = new List<string>();
        if (seatedPlayers == null || seatedPlayers.Count == 0)
            return order;

        var button = ((buttonSeat % seatedPlayers.Count) + seatedPlayers.Count)
                     % seatedPlayers.Count;
        for (var step = 1; step <= seatedPlayers.Count; step++)
            order.Add(seatedPlayers[(button + step) % seatedPlayers.Count]);

        return order;
    }

    /// <summary>Heads-up, the button IS the small blind.</summary>
    public static int SmallBlindSeat(int buttonSeat, int seatCount) =>
        seatCount == 2 ? buttonSeat : Next(buttonSeat, seatCount);

    public static int BigBlindSeat(int buttonSeat, int seatCount) =>
        Next(SmallBlindSeat(buttonSeat, seatCount), seatCount);

    /// <summary>Before the flop: left of the big blind, or the button when heads-up.</summary>
    public static int FirstToActPreflop(int buttonSeat, int seatCount) =>
        seatCount == 2
            ? buttonSeat
            : Next(BigBlindSeat(buttonSeat, seatCount), seatCount);

    /// <summary>After the flop: left of the button, which heads-up is the big blind.</summary>
    public static int FirstToActPostflop(int buttonSeat, int seatCount) =>
        seatCount == 2
            ? BigBlindSeat(buttonSeat, seatCount)
            : Next(buttonSeat, seatCount);

    /// <summary>
    /// Going round from <paramref name="fromSeat"/> (exclusive), the next player who can still make
    /// a decision, or -1 when nobody can. Folded and all-in players are stepped over.
    /// </summary>
    public static int NextAbleToAct(IReadOnlyList<PlayerBetState> players, int fromSeat)
    {
        if (players == null || players.Count == 0)
            return -1;

        for (var step = 1; step <= players.Count; step++)
        {
            var seat = (fromSeat + step) % players.Count;
            if (players[seat].CanAct)
                return seat;
        }

        return -1;
    }

    /// <summary>
    /// From <paramref name="seat"/> INCLUSIVE — used to open a street, where the nominal first
    /// speaker may already be all-in from a previous one.
    /// </summary>
    public static int FirstAbleToActFrom(IReadOnlyList<PlayerBetState> players, int seat)
    {
        if (players == null || players.Count == 0)
            return -1;

        for (var step = 0; step < players.Count; step++)
        {
            var candidate = (seat + step) % players.Count;
            if (players[candidate].CanAct)
                return candidate;
        }

        return -1;
    }

    /// <summary>
    /// Seat order for handing out a chip that will not divide: starting left of the button, which is
    /// where a real dealer starts. Any fixed order would do; what matters is that it never changes.
    /// </summary>
    public static List<string> OddChipOrder(IReadOnlyList<string> seatedPlayers, int buttonSeat)
    {
        var order = new List<string>();
        if (seatedPlayers == null || seatedPlayers.Count == 0)
            return order;

        for (var step = 1; step <= seatedPlayers.Count; step++)
            order.Add(seatedPlayers[(buttonSeat + step) % seatedPlayers.Count]);

        return order;
    }

    private static int NextSeatWithChips(
        IReadOnlyList<PlayerBetState> players, int fromSeat)
    {
        for (var step = 1; step <= players.Count; step++)
        {
            var seat = (fromSeat + step) % players.Count;
            if (players[seat].Stack > 0)
                return seat;
        }

        return fromSeat;
    }
}
