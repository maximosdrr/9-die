using System.Collections.Generic;
using Godot;

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

	/// <summary>
	/// Builds a practical table bank rather than the smallest possible greedy representation.
	/// Keeping a reserve of small chips lets ordinary calls and raises remove real visible chips
	/// without turning one large chip into a handful of smaller ones in the middle of an action.
	/// Zero-count runs are intentional: they reserve stable columns for the whole hand.
	/// </summary>
	public static List<ChipRun> CreatePlayableBank(int amount)
	{
		var counts = new Dictionary<int, int>();
		var remaining = Mathf.Max(0, amount);
		if (remaining == 0)
			return new List<ChipRun>();

		// Preserve the exact remainder first, then enough 5/10/25 chips to cover every common
		// betting increment. Larger values are filled greedily after that reserve exists.
		TakeReserve(counts, 1, remaining % 5, ref remaining);
		TakeReserve(counts, 5, 4, ref remaining);
		TakeReserve(counts, 10, 4, ref remaining);
		TakeReserve(counts, 25, 2, ref remaining);

		foreach (var denomination in Denominations)
		{
			if (remaining < denomination)
				continue;

			var count = remaining / denomination;
			remaining -= count * denomination;
			counts[denomination] = counts.GetValueOrDefault(denomination) + count;
		}

		var largest = 1;
		foreach (var entry in counts)
		{
			if (entry.Value > 0)
				largest = Mathf.Max(largest, entry.Key);
		}

		var smallest = amount % 5 == 0 ? 5 : 1;
		var bank = new List<ChipRun>();
		foreach (var denomination in Denominations)
		{
			if (denomination > largest || denomination < smallest)
				continue;

			bank.Add(new ChipRun(denomination, counts.GetValueOrDefault(denomination)));
		}

		return bank;
	}

	/// <summary>
	/// Removes an exact payment from a stable bank. The bounded search is deliberate: greedy choice
	/// alone can fail when (for example) a 25 must be left aside in favour of three 10s.
	/// Run order and zero-count lanes are preserved so every remaining visual keeps its column.
	/// </summary>
	public static bool TryTake(List<ChipRun> bank, int amount, out List<ChipRun> payment)
	{
		payment = new List<ChipRun>();
		if (bank == null || amount < 0 || amount > Total(bank))
			return false;
		if (amount == 0)
			return true;

		var take = new int[bank.Count];
		if (!SearchTake(bank, 0, amount, take))
			return false;

		for (var i = 0; i < bank.Count; i++)
		{
			if (take[i] <= 0)
				continue;

			var run = bank[i];
			payment.Add(new ChipRun(run.Denomination, take[i]));
			bank[i] = new ChipRun(run.Denomination, run.Count - take[i]);
		}

		return true;
	}

	private static void TakeReserve(
		Dictionary<int, int> counts, int denomination, int wanted, ref int remaining)
	{
		var count = Mathf.Min(Mathf.Max(0, wanted), remaining / denomination);
		if (count <= 0)
			return;

		counts[denomination] = counts.GetValueOrDefault(denomination) + count;
		remaining -= count * denomination;
	}

	private static bool SearchTake(List<ChipRun> bank, int index, int remaining, int[] take)
	{
		if (remaining == 0)
			return true;
		if (index >= bank.Count || remaining < 0)
			return false;

		var run = bank[index];
		var maximum = Mathf.Min(run.Count, remaining / run.Denomination);
		for (var count = maximum; count >= 0; count--)
		{
			take[index] = count;
			if (SearchTake(bank, index + 1, remaining - count * run.Denomination, take))
				return true;
		}

		take[index] = 0;
		return false;
	}
}
