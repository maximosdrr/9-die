using System.Collections.Generic;

namespace Poker.Rules;

/// <summary>Why a physically prepared wager cannot be pushed into the pot yet.</summary>
public enum PokerWagerProblem
{
    None,
    NoChipsSelected,
    BelowMinimum,
    AboveMaximum,
    ActionUnavailable,
}

/// <summary>
/// Converts chips selected on the cloth into the same action/total pair validated by the server.
/// Keeping this pure prevents the physical interface from inventing a second set of betting rules.
/// </summary>
public static class PokerWagerInteraction
{
    /// <summary>
    /// Resolves the physical PASSAR button before looking at a tentative wager. A legal check always
    /// wins: selected chips are returned automatically. When a call is required the same chips remain
    /// selected, allowing the player to add the missing value instead of entering a contradictory loop.
    /// </summary>
    public static bool TryPrepareCheck(
        IReadOnlyList<ActionOption> options, int selectedAmount, out bool returnSelectedChips)
    {
        returnSelectedChips = false;
        if (!Find(options, PokerActionKind.Check).HasValue)
            return false;

        returnSelectedChips = selectedAmount > 0;
        return true;
    }

    public static bool TryResolve(
        IReadOnlyList<ActionOption> options,
        int committedThisRound,
        int selectedAmount,
        out PokerActionKind kind,
        out int total,
        out PokerWagerProblem problem,
        out int requiredSelectedAmount)
    {
        kind = PokerActionKind.None;
        total = committedThisRound + System.Math.Max(0, selectedAmount);
        problem = PokerWagerProblem.ActionUnavailable;
        requiredSelectedAmount = 0;

        if (selectedAmount <= 0)
        {
            problem = PokerWagerProblem.NoChipsSelected;
            return false;
        }

        var call = Find(options, PokerActionKind.Call);
        if (call.HasValue && total == call.Value.MinTotal)
        {
            kind = PokerActionKind.Call;
            problem = PokerWagerProblem.None;
            return true;
        }

        if (call.HasValue && total < call.Value.MinTotal)
        {
            problem = PokerWagerProblem.BelowMinimum;
            requiredSelectedAmount = call.Value.MinTotal - committedThisRound;
            return false;
        }

        var raise = Find(options, PokerActionKind.Raise);
        if (raise.HasValue && raise.Value.Allows(total))
        {
            kind = PokerActionKind.Raise;
            problem = PokerWagerProblem.None;
            return true;
        }

        if (raise.HasValue && total < raise.Value.MinTotal)
        {
            problem = PokerWagerProblem.BelowMinimum;
            requiredSelectedAmount = raise.Value.MinTotal - committedThisRound;
            return false;
        }

        if (raise.HasValue && total > raise.Value.MaxTotal)
        {
            problem = PokerWagerProblem.AboveMaximum;
            requiredSelectedAmount = raise.Value.MaxTotal - committedThisRound;
            return false;
        }

        return false;
    }

    private static ActionOption? Find(
        IReadOnlyList<ActionOption> options, PokerActionKind kind)
    {
        if (options == null)
            return null;

        foreach (var option in options)
        {
            if (option.Kind == kind)
                return option;
        }

        return null;
    }
}
