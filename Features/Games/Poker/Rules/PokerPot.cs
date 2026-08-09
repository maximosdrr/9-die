using System.Collections.Generic;
using System.Linq;

namespace Poker.Rules;

/// <summary>One pot and the players still able to win it.</summary>
public readonly struct Pot
{
	public readonly int Amount;
	public readonly IReadOnlyList<string> EligiblePlayers;

	public Pot(int amount, IReadOnlyList<string> eligiblePlayers)
	{
		Amount = amount;
		EligiblePlayers = eligiblePlayers;
	}
}

/// <summary>
/// Turning what everyone put in into what everyone takes out.
///
/// The hard part is the SIDE POT. A player who is all-in for less than the others can only win the
/// part of the pot they could actually cover; the excess is contested by the players who could keep
/// betting. Getting this wrong is not a rounding error — it hands someone chips that were never
/// theirs to win, and it compounds across a session.
///
/// Two invariants the tests pin, because they are what "the money is conserved" means here:
///   * the pots always sum to exactly what was contributed, and
///   * the awards always sum to exactly the pots.
/// An odd chip that will not divide goes by a fixed seat order rather than by dictionary iteration,
/// so a split pot pays the same way every time.
/// </summary>
public static class PokerPot
{
	/// <summary>
	/// Splits every contribution into a main pot and however many side pots the all-ins require.
	///
	/// <paramref name="contributions"/> includes players who folded — their chips stay in — while
	/// <paramref name="contenders"/> is who can still win.
	/// </summary>
	public static List<Pot> Build(
		IReadOnlyDictionary<string, int> contributions,
		IReadOnlyCollection<string> contenders)
	{
		var pots = new List<Pot>();
		if (contributions == null || contributions.Count == 0)
			return pots;

		var levels = contributions.Values
			.Where(amount => amount > 0)
			.Distinct()
			.OrderBy(amount => amount)
			.ToList();

		var previous = 0;

		foreach (var level in levels)
		{
			// Everyone pays into this layer up to whatever they could cover.
			var layer = contributions.Values
				.Sum(amount => System.Math.Max(0, System.Math.Min(amount, level) - previous));

			previous = level;

			if (layer <= 0)
				continue;

			var eligible = contenders == null
				? new List<string>()
				: contenders
					.Where(player => contributions.TryGetValue(player, out var paid) && paid >= level)
					.ToList();

			// Nobody left who could win this layer — it belongs to the players below it. This is the
			// money a folded player left behind above every remaining contender's all-in.
			if (eligible.Count == 0)
			{
				if (pots.Count > 0)
					pots[^1] = new Pot(pots[^1].Amount + layer, pots[^1].EligiblePlayers);
				else
					pots.Add(new Pot(layer, new List<string>()));

				continue;
			}

			// A level that only a folded player sat on does not start a new pot.
			if (pots.Count > 0 && SameSet(pots[^1].EligiblePlayers, eligible))
			{
				pots[^1] = new Pot(pots[^1].Amount + layer, pots[^1].EligiblePlayers);
				continue;
			}

			pots.Add(new Pot(layer, eligible));
		}

		return pots;
	}

	/// <summary>
	/// Gives each pot to the best hand among the players eligible for it, splitting exact ties.
	///
	/// <paramref name="oddChipOrder"/> decides who gets a chip that will not divide — the real game
	/// starts left of the dealer, and any fixed order will do as long as it is the same every time.
	/// </summary>
	public static Dictionary<string, int> Award(
		IReadOnlyList<Pot> pots,
		IReadOnlyDictionary<string, PokerHandRank> ranks,
		IReadOnlyList<string> oddChipOrder)
	{
		var winnings = new Dictionary<string, int>();
		if (pots == null)
			return winnings;

		foreach (var pot in pots)
		{
			if (pot.Amount <= 0 || pot.EligiblePlayers == null || pot.EligiblePlayers.Count == 0)
				continue;

			var best = PokerHandRank.None;
			foreach (var player in pot.EligiblePlayers)
			{
				if (ranks != null && ranks.TryGetValue(player, out var rank) && rank > best)
					best = rank;
			}

			var winners = pot.EligiblePlayers
				.Where(player => ranks != null && ranks.TryGetValue(player, out var rank) && rank == best)
				.ToList();

			if (winners.Count == 0)
				continue;

			var share = pot.Amount / winners.Count;
			var remainder = pot.Amount - share * winners.Count;

			foreach (var winner in winners)
				winnings[winner] = winnings.GetValueOrDefault(winner) + share;

			var ordered = winners.OrderBy(winner => SeatIndex(oddChipOrder, winner)).ToList();
			for (var i = 0; i < remainder && i < ordered.Count; i++)
				winnings[ordered[i]] += 1;
		}

		return winnings;
	}

	public static int Total(IReadOnlyList<Pot> pots) =>
		pots == null ? 0 : pots.Sum(pot => pot.Amount);

	private static int SeatIndex(IReadOnlyList<string> order, string player)
	{
		if (order == null)
			return 0;

		for (var i = 0; i < order.Count; i++)
		{
			if (order[i] == player)
				return i;
		}

		return order.Count;
	}

	private static bool SameSet(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
		a.Count == b.Count && !a.Except(b).Any();
}
