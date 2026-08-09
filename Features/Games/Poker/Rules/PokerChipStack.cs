using System.Collections.Generic;

namespace Poker.Rules;

/// <summary>How many chips of one value sit in a stack.</summary>
public readonly struct ChipRun
{
	public readonly int Denomination;
	public readonly int Count;

	public ChipRun(int denomination, int count)
	{
		Denomination = denomination;
		Count = count;
	}

	public int Value => Denomination * Count;
}

/// <summary>
/// Turns a number of chips into the physical chips that make it up.
///
/// This exists because the game shows no numbers on a panel: a bet is a stack of chips on the
/// cloth, and it has to be HONEST — a player counting 2x100 + 1x25 must be looking at 225. The
/// decomposition is greedy from the largest denomination down, which is also how a dealer stacks it,
/// so what the player sees matches what they would see at a real table.
/// </summary>
public static class PokerChipStack
{
	/// <summary>Largest first — the order the decomposition walks. Matches the art pack's set.</summary>
	public static readonly int[] Denominations = { 10000, 5000, 1000, 500, 100, 50, 25, 10, 5, 1 };

	public static int SmallestDenomination => Denominations[Denominations.Length - 1];

	/// <summary>
	/// The chips making up <paramref name="amount"/>, largest first.
	///
	/// <paramref name="maxRuns"/> caps how many DIFFERENT denominations are rendered; the remainder
	/// is folded into the last one as a slight over-count rather than dropped, because a stack that
	/// silently shows less than it is worth is worse than one that is a chip out of true.
	/// </summary>
	public static List<ChipRun> Decompose(int amount, int maxRuns = 0)
	{
		var runs = new List<ChipRun>();
		if (amount <= 0)
			return runs;

		var remaining = amount;

		foreach (var denomination in Denominations)
		{
			if (remaining < denomination)
				continue;

			var count = remaining / denomination;
			remaining -= count * denomination;

			runs.Add(new ChipRun(denomination, count));

			if (maxRuns > 0 && runs.Count == maxRuns && remaining > 0)
			{
				// Round the tail up into this denomination so the stack is never worth less than
				// the number it represents.
				var extra = (remaining + denomination - 1) / denomination;
				runs[^1] = new ChipRun(denomination, count + extra);
				remaining = 0;
				break;
			}

			if (remaining == 0)
				break;
		}

		return runs;
	}

	public static int Total(IReadOnlyList<ChipRun> runs)
	{
		if (runs == null)
			return 0;

		var total = 0;
		foreach (var run in runs)
			total += run.Value;

		return total;
	}

	/// <summary>How many chips are in the stack altogether — what the presenter has to draw.</summary>
	public static int ChipCount(IReadOnlyList<ChipRun> runs)
	{
		if (runs == null)
			return 0;

		var count = 0;
		foreach (var run in runs)
			count += run.Count;

		return count;
	}

	/// <summary>The largest denomination worth no more than <paramref name="amount"/>.</summary>
	public static int LargestFitting(int amount)
	{
		foreach (var denomination in Denominations)
		{
			if (denomination <= amount)
				return denomination;
		}

		return SmallestDenomination;
	}
}
