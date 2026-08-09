using System;
using System.Collections.Generic;
using System.Linq;
using Poker.Rules;

/// <summary>
/// Pure payout planning. It decides who receives each existing physical group and, when those
/// groups cannot express the authoritative awards exactly, describes the dealer change required.
/// No scene nodes, animation state or networking enter this component.
/// </summary>
public static class PokerPayoutPlanner
{
	public sealed class WinnerPlan
	{
		public string PlayerId = "";
		public int Amount;
		public List<ChipRun> ExactRuns = new();
	}

	public sealed class Plan
	{
		public string[] ExistingRecipients = Array.Empty<string>();
		public List<WinnerPlan> Winners = new();
		public bool RequiresDealerChange;
		public int TotalAward => Winners.Sum(winner => winner.Amount);
	}

	public static Plan Create(
		IReadOnlyList<int> physicalGroupValues,
		IReadOnlyDictionary<string, int> awards,
		IReadOnlyList<string> seatOrder)
	{
		var values = physicalGroupValues ?? Array.Empty<int>();
		var plan = new Plan
		{
			ExistingRecipients = AssignWholeGroups(values, awards, seatOrder),
		};
		if (awards == null)
			return plan;

		plan.Winners = awards.Where(entry => entry.Value > 0)
			.OrderBy(entry => SeatIndexOf(seatOrder, entry.Key))
			.Select(entry => new WinnerPlan
			{
				PlayerId = entry.Key,
				Amount = entry.Value,
				ExactRuns = PokerChipStack.Decompose(entry.Value),
			}).ToList();

		var delivered = plan.Winners.ToDictionary(winner => winner.PlayerId, _ => 0);
		for (var i = 0; i < values.Count && i < plan.ExistingRecipients.Length; i++)
		{
			var recipient = plan.ExistingRecipients[i];
			if (delivered.ContainsKey(recipient))
				delivered[recipient] += values[i];
		}

		plan.RequiresDealerChange = values.Sum() != plan.TotalAward
			|| plan.Winners.Any(winner => delivered.GetValueOrDefault(winner.PlayerId) != winner.Amount);
		return plan;
	}

	/// <summary>
	/// Keeps existing groups intact whenever possible. The result is only accepted as an exact payout
	/// by <see cref="Create"/>; otherwise it becomes the path into the dealer-change presentation.
	/// </summary>
	public static string[] AssignWholeGroups(
		IReadOnlyList<int> groupValues, IReadOnlyDictionary<string, int> awards,
		IReadOnlyList<string> seatOrder)
	{
		if (groupValues == null || awards == null)
			return Array.Empty<string>();

		var winners = awards.Where(entry => entry.Value > 0)
			.OrderBy(entry => SeatIndexOf(seatOrder, entry.Key)).ToList();
		if (winners.Count == 0)
			return new string[groupValues.Count];

		var remaining = winners.ToDictionary(entry => entry.Key, entry => entry.Value);
		var recipients = Enumerable.Repeat("", groupValues.Count).ToArray();
		var available = Enumerable.Range(0, groupValues.Count).ToList();

		foreach (var winner in winners.OrderBy(entry => entry.Value))
		{
			if (available.Count == 0)
				break;
			var chosen = available.Where(index => groupValues[index] <= winner.Value)
				.OrderByDescending(index => groupValues[index]).FirstOrDefault(-1);
			if (chosen < 0)
				chosen = available[^1];
			recipients[chosen] = winner.Key;
			remaining[winner.Key] -= groupValues[chosen];
			available.Remove(chosen);
		}

		foreach (var index in available)
		{
			var value = groupValues[index];
			var fitting = winners.Where(entry => remaining[entry.Key] >= value)
				.OrderByDescending(entry => remaining[entry.Key]).FirstOrDefault();
			var winnerId = !string.IsNullOrEmpty(fitting.Key)
				? fitting.Key
				: winners.OrderByDescending(entry => remaining[entry.Key]).First().Key;
			recipients[index] = winnerId;
			remaining[winnerId] -= value;
		}
		return recipients;
	}

	private static int SeatIndexOf(IReadOnlyList<string> seats, string playerId)
	{
		if (seats == null)
			return int.MaxValue;
		for (var i = 0; i < seats.Count; i++)
		{
			if (seats[i] == playerId)
				return i;
		}
		return int.MaxValue;
	}
}
