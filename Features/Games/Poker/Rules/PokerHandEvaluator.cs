using System.Collections.Generic;

namespace Poker.Rules;

/// <summary>
/// The best five-card hand available from a player's cards, as a comparable rank.
///
/// Works on any number of cards from five up, so it covers Hold'em's seven (two private plus five
/// community) and also a short board mid-hand. It never enumerates the 21 five-card subsets: it
/// counts ranks and suits once and reads the answer off those counts, which is both faster and
/// easier to check by eye against the rules.
///
/// The two things that are always got wrong, both pinned by tests: the WHEEL (A-2-3-4-5 is the
/// LOWEST straight, not an ace-high one) and KICKERS (two players with the same pair are separated
/// by their remaining cards, and if those match too it is a genuine tie that splits the pot).
/// </summary>
public static class PokerHandEvaluator
{
	public static PokerHandRank Evaluate(IReadOnlyList<int> holeCards, IReadOnlyList<int> board)
	{
		var all = new List<int>(7);
		if (holeCards != null)
			all.AddRange(holeCards);
		if (board != null)
			all.AddRange(board);

		return Evaluate(all);
	}

	public static PokerHandRank Evaluate(IReadOnlyList<int> cards)
	{
		if (cards == null || cards.Count < 5)
			return PokerHandRank.None;

		var rankCount = new int[CardId.Ranks];
		var suitCount = new int[CardId.Suits];
		var suitRanks = new int[CardId.Suits];
		var rankMask = 0;

		foreach (var card in cards)
		{
			if (!CardId.IsValid(card))
				continue;

			var rank = CardId.RankOf(card);
			var suit = CardId.SuitOf(card);

			rankCount[rank]++;
			suitCount[suit]++;
			suitRanks[suit] |= 1 << rank;
			rankMask |= 1 << rank;
		}

		// A seven-card hand can only ever hold one flush suit, so the first one found is the one.
		var flushSuit = -1;
		for (var suit = 0; suit < CardId.Suits; suit++)
		{
			if (suitCount[suit] < 5)
				continue;

			flushSuit = suit;
			break;
		}

		if (flushSuit >= 0)
		{
			var straightFlushHigh = StraightHigh(suitRanks[flushSuit]);
			if (straightFlushHigh >= 0)
				return new PokerHandRank(HandCategory.StraightFlush, straightFlushHigh);
		}

		var quad = HighestRankWithCount(rankCount, 4);
		if (quad >= 0)
			return new PokerHandRank(HandCategory.FourOfAKind, quad, HighestExcluding(rankCount, quad));

		var trips = HighestRankWithCount(rankCount, 3);
		if (trips >= 0)
		{
			// The pair of a full house may be a SECOND set of trips, so look for a lower trips too.
			var pair = HighestRankWithCount(rankCount, 2);
			var lowerTrips = HighestRankWithCount(rankCount, 3, below: trips);
			var support = Higher(pair, lowerTrips);

			if (support >= 0)
				return new PokerHandRank(HandCategory.FullHouse, trips, support);
		}

		if (flushSuit >= 0)
		{
			var flush = TopRanks(suitRanks[flushSuit], 5);
			return new PokerHandRank(HandCategory.Flush,
				flush[0], flush[1], flush[2], flush[3], flush[4]);
		}

		var straightHigh = StraightHigh(rankMask);
		if (straightHigh >= 0)
			return new PokerHandRank(HandCategory.Straight, straightHigh);

		if (trips >= 0)
		{
			var kickers = TopRanksExcluding(rankCount, 2, trips);
			return new PokerHandRank(HandCategory.ThreeOfAKind, trips, kickers[0], kickers[1]);
		}

		var highPair = HighestRankWithCount(rankCount, 2);
		if (highPair >= 0)
		{
			var lowPair = HighestRankWithCount(rankCount, 2, below: highPair);

			if (lowPair >= 0)
			{
				var kicker = TopRanksExcluding(rankCount, 1, highPair, lowPair);
				return new PokerHandRank(HandCategory.TwoPair, highPair, lowPair, kicker[0]);
			}

			var pairKickers = TopRanksExcluding(rankCount, 3, highPair);
			return new PokerHandRank(HandCategory.Pair,
				highPair, pairKickers[0], pairKickers[1], pairKickers[2]);
		}

		var high = TopRanks(rankMask, 5);
		return new PokerHandRank(HandCategory.HighCard,
			high[0], high[1], high[2], high[3], high[4]);
	}

	/// <summary>
	/// The top card of the best straight in a rank mask, or -1.
	///
	/// The wheel is checked last and answers with the FIVE, so A-2-3-4-5 sits below 6-high where it
	/// belongs rather than being read as an ace-high straight.
	/// </summary>
	private static int StraightHigh(int rankMask)
	{
		for (var high = CardId.Ace; high >= 4; high--)
		{
			var run = 0;
			for (var step = 0; step < 5; step++)
				run |= 1 << (high - step);

			if ((rankMask & run) == run)
				return high;
		}

		const int Wheel = (1 << CardId.Ace) | (1 << 3) | (1 << 2) | (1 << 1) | (1 << 0);
		return (rankMask & Wheel) == Wheel ? CardId.Five : -1;
	}

	private static int HighestRankWithCount(int[] rankCount, int count, int below = CardId.Ranks)
	{
		for (var rank = System.Math.Min(below - 1, CardId.Ranks - 1); rank >= 0; rank--)
		{
			if (rankCount[rank] == count)
				return rank;
		}

		return -1;
	}

	private static int Higher(int a, int b) => a > b ? a : b;

	private static int HighestExcluding(int[] rankCount, int excluded)
	{
		for (var rank = CardId.Ranks - 1; rank >= 0; rank--)
		{
			if (rank != excluded && rankCount[rank] > 0)
				return rank;
		}

		return 0;
	}

	/// <summary>Highest distinct ranks present in a mask, descending, padded with zeroes.</summary>
	private static int[] TopRanks(int rankMask, int wanted)
	{
		var result = new int[wanted];
		var found = 0;

		for (var rank = CardId.Ranks - 1; rank >= 0 && found < wanted; rank--)
		{
			if ((rankMask & (1 << rank)) != 0)
				result[found++] = rank;
		}

		return result;
	}

	private static int[] TopRanksExcluding(int[] rankCount, int wanted, params int[] excluded)
	{
		var result = new int[wanted];
		var found = 0;

		for (var rank = CardId.Ranks - 1; rank >= 0 && found < wanted; rank--)
		{
			if (rankCount[rank] == 0 || System.Array.IndexOf(excluded, rank) >= 0)
				continue;

			result[found++] = rank;
		}

		return result;
	}
}
