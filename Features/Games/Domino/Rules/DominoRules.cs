using System.Collections.Generic;

namespace Domino.Rules;

/// <summary>
/// The rules of a double-six individual match, as pure functions over ids. The client calls these
/// to build the hand UI and the server calls the SAME functions to validate what comes back, so a
/// move the interface offered can never be one the server rejects, and a move the interface hid
/// can never be smuggled through by a modified client.
/// </summary>
public static class DominoRules
{
	/// <summary>
	/// Whether a tile may be laid against an end. An empty board takes any tile — the opening
	/// move has no end to match, so <paramref name="end"/> is ignored there.
	/// </summary>
	public static bool CanPlace(int tileId, ChainEnd end, int leftEnd, int rightEnd)
	{
		if (!DominoTileId.IsValid(tileId))
			return false;

		if (leftEnd == DominoTileId.NoEnd && rightEnd == DominoTileId.NoEnd)
			return true;

		var matched = end == ChainEnd.Left ? leftEnd : rightEnd;
		return matched != DominoTileId.NoEnd && DominoTileId.Matches(tileId, matched);
	}

	/// <summary>
	/// Whether a tile HELD WITH <paramref name="leadingPips"/> FACING THE CHAIN fits the aimed end.
	///
	/// This is a client-side ergonomics gate, not a rule of the game: which half meets the chain is
	/// forced by the end being played on, so the server keeps validating plain
	/// <see cref="CanPlace"/> and nothing extra travels. What this adds is the requirement that the
	/// player physically turned the tile the right way round before laying it — with 3|5 in hand and
	/// ends 3 and 5, leading with the 3 while aiming at the 5 end is refused even though the tile
	/// itself is perfectly legal there.
	/// </summary>
	public static bool CanPlaceOriented(int tileId, int leadingPips, ChainEnd end, int leftEnd, int rightEnd)
	{
		if (!DominoTileId.IsValid(tileId) || !DominoTileId.Matches(tileId, leadingPips))
			return false;

		// The opening tile meets nothing, so any half may lead.
		if (leftEnd == DominoTileId.NoEnd && rightEnd == DominoTileId.NoEnd)
			return true;

		var matched = end == ChainEnd.Left ? leftEnd : rightEnd;
		return matched != DominoTileId.NoEnd && matched == leadingPips;
	}

	/// <summary>
	/// Everything the hand could do this turn. A tile that fits both ends appears twice, because
	/// the two placements land in different spots on the table; on an empty board each tile
	/// appears once, against the right end by convention.
	/// </summary>
	public static List<MoveOption> LegalMoves(IEnumerable<int> hand, int leftEnd, int rightEnd)
	{
		var moves = new List<MoveOption>();
		var opening = leftEnd == DominoTileId.NoEnd && rightEnd == DominoTileId.NoEnd;

		foreach (var tileId in hand)
		{
			if (!DominoTileId.IsValid(tileId))
				continue;

			if (opening)
			{
				moves.Add(new MoveOption(tileId, ChainEnd.Right));
				continue;
			}

			if (CanPlace(tileId, ChainEnd.Left, leftEnd, rightEnd))
				moves.Add(new MoveOption(tileId, ChainEnd.Left));

			if (CanPlace(tileId, ChainEnd.Right, leftEnd, rightEnd))
				moves.Add(new MoveOption(tileId, ChainEnd.Right));
		}

		return moves;
	}

	public static bool HasLegalMove(IEnumerable<int> hand, int leftEnd, int rightEnd)
	{
		if (leftEnd == DominoTileId.NoEnd && rightEnd == DominoTileId.NoEnd)
		{
			foreach (var tileId in hand)
			{
				if (DominoTileId.IsValid(tileId))
					return true;
			}

			return false;
		}

		foreach (var tileId in hand)
		{
			if (CanPlace(tileId, ChainEnd.Left, leftEnd, rightEnd)
				|| CanPlace(tileId, ChainEnd.Right, leftEnd, rightEnd))
				return true;
		}

		return false;
	}

	/// <summary>Whether the player may draw: stuck, and the boneyard still has something.</summary>
	public static bool CanDraw(IEnumerable<int> hand, int leftEnd, int rightEnd, int boneyardCount) =>
		boneyardCount > 0 && !HasLegalMove(hand, leftEnd, rightEnd);

	/// <summary>Whether the player must pass: stuck with nothing left to draw.</summary>
	public static bool MustPass(IEnumerable<int> hand, int leftEnd, int rightEnd, int boneyardCount) =>
		boneyardCount == 0 && !HasLegalMove(hand, leftEnd, rightEnd);

	public static int PipTotal(IEnumerable<int> tiles)
	{
		var total = 0;
		foreach (var tileId in tiles)
		{
			if (DominoTileId.IsValid(tileId))
				total += DominoTileId.Pips(tileId);
		}

		return total;
	}

	/// <summary>
	/// Whether the match is locked: the boneyard is spent and nobody left in the match can move.
	/// </summary>
	public static bool IsLocked(
		IReadOnlyDictionary<string, List<int>> hands,
		IReadOnlyList<string> turnOrder,
		int leftEnd,
		int rightEnd,
		int boneyardCount)
	{
		if (boneyardCount > 0)
			return false;

		foreach (var playerId in turnOrder)
		{
			if (hands.TryGetValue(playerId, out var hand) && HasLegalMove(hand, leftEnd, rightEnd))
				return false;
		}

		return true;
	}

	/// <summary>
	/// Who takes a locked game: the lowest pip total in hand. Ties go to whoever sits nearest
	/// after the player who blocked the game, so the outcome never depends on dictionary order.
	/// Returns null only when there is nobody left to win.
	/// </summary>
	public static string ResolveLock(
		IReadOnlyDictionary<string, int> pipTotals,
		IReadOnlyList<string> turnOrder,
		string lastPlayerId)
	{
		var lowest = int.MaxValue;
		foreach (var playerId in turnOrder)
		{
			if (pipTotals.TryGetValue(playerId, out var total) && total < lowest)
				lowest = total;
		}

		if (lowest == int.MaxValue)
			return null;

		// -1 when the blocker already left the match, which starts the sweep at the first seat.
		var blockerIndex = -1;
		for (var i = 0; i < turnOrder.Count; i++)
		{
			if (turnOrder[i] == lastPlayerId)
			{
				blockerIndex = i;
				break;
			}
		}

		for (var step = 1; step <= turnOrder.Count; step++)
		{
			var index = (blockerIndex + step) % turnOrder.Count;
			var playerId = turnOrder[index];

			if (pipTotals.TryGetValue(playerId, out var total) && total == lowest)
				return playerId;
		}

		return null;
	}
}
