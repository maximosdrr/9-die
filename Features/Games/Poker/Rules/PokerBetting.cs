using System.Collections.Generic;

namespace Poker.Rules;

public enum PokerActionKind
{
	/// <summary>
	/// No decision. First on purpose, so <c>default(ActionOption)</c> is "nothing" rather than
	/// FOLD — an uninitialised struct must never read as the one action that throws a hand away.
	/// </summary>
	None = 0,

	Fold = 1,
	Check = 2,
	Call = 3,
	Raise = 4,
}

/// <summary>Everything a betting round needs to know about one player.</summary>
public sealed class PlayerBetState
{
	public string PlayerId = "";

	/// <summary>Chips still in front of them.</summary>
	public int Stack;

	/// <summary>Chips pushed in during the CURRENT street.</summary>
	public int CommittedThisRound;

	/// <summary>Chips pushed in across the whole hand — this is what builds the pots.</summary>
	public int CommittedThisHand;

	public bool HasFolded;

	/// <summary>Whether they have had a turn since the last raise reopened the action.</summary>
	public bool HasActedThisRound;

	/// <summary>Out of chips but still in the hand: they see every remaining card for free.</summary>
	public bool IsAllIn => !HasFolded && Stack <= 0;

	/// <summary>Able to make a decision — not folded, and with something left to bet.</summary>
	public bool CanAct => !HasFolded && Stack > 0;
}

/// <summary>An action the player may take, and the range of totals it allows.</summary>
public readonly struct ActionOption
{
	public readonly PokerActionKind Kind;

	/// <summary>Least this player would have committed THIS ROUND after acting.</summary>
	public readonly int MinTotal;

	/// <summary>Most they could commit this round — for a raise, their whole stack.</summary>
	public readonly int MaxTotal;

	public ActionOption(PokerActionKind kind, int minTotal, int maxTotal)
	{
		Kind = kind;
		MinTotal = minTotal;
		MaxTotal = maxTotal;
	}

	public bool Allows(int total) => total >= MinTotal && total <= MaxTotal;
}

/// <summary>
/// What a player may do right now, and when the street is over.
///
/// Amounts are always the player's TOTAL for the current street, never the chips added. One
/// convention throughout removes the class of bug where the interface offers "raise to 60" and the
/// server reads "raise by 60".
///
/// Called by the client to build the choices and by the server to check what came back, which is
/// what stops the interface from ever offering something the server would refuse.
/// </summary>
public static class PokerBetting
{
	/// <summary>Chips this player must add to match the bet, capped by what they have.</summary>
	public static int AmountToCall(PlayerBetState player, int currentBet)
	{
		if (player == null)
			return 0;

		return System.Math.Max(0, System.Math.Min(currentBet - player.CommittedThisRound, player.Stack));
	}

	/// <summary>Everything they have, as a street total. A raise can never exceed this.</summary>
	public static int MaxTotal(PlayerBetState player) =>
		player == null ? 0 : player.CommittedThisRound + player.Stack;

	/// <summary>
	/// Smallest legal raise: the current bet plus the last full raise. Clamped to the player's stack,
	/// because being unable to afford a full raise never stops anyone from going all-in.
	/// </summary>
	public static int MinRaiseTotal(PlayerBetState player, int currentBet, int minRaiseIncrement) =>
		System.Math.Min(currentBet + System.Math.Max(minRaiseIncrement, 1), MaxTotal(player));

	/// <summary>
	/// A raise that reaches at least the full increment. A SHORT all-in does not reopen the betting
	/// for players who have already acted — they may still call it, but they do not get a fresh
	/// chance to re-raise.
	/// </summary>
	public static bool IsFullRaise(int raiseTotal, int currentBet, int minRaiseIncrement) =>
		raiseTotal - currentBet >= System.Math.Max(minRaiseIncrement, 1);

	public static List<ActionOption> LegalActions(PlayerBetState player, int currentBet, int minRaiseIncrement)
	{
		var options = new List<ActionOption>();
		if (player == null || !player.CanAct)
			return options;

		var toCall = AmountToCall(player, currentBet);
		var maxTotal = MaxTotal(player);

		if (toCall > 0)
		{
			// Folding is only offered when it costs something to stay. Presenting "fold" next to a
			// free check is how players throw away hands they meant to keep.
			options.Add(new ActionOption(PokerActionKind.Fold, 0, 0));
			options.Add(new ActionOption(
				PokerActionKind.Call,
				player.CommittedThisRound + toCall,
				player.CommittedThisRound + toCall));
		}
		else
		{
			options.Add(new ActionOption(
				PokerActionKind.Check, player.CommittedThisRound, player.CommittedThisRound));
		}

		// Only worth offering if they can actually get above the current bet.
		if (maxTotal > currentBet)
		{
			options.Add(new ActionOption(
				PokerActionKind.Raise,
				MinRaiseTotal(player, currentBet, minRaiseIncrement),
				maxTotal));
		}

		return options;
	}

	/// <summary>
	/// Whether <paramref name="kind"/> at <paramref name="total"/> is something the server will take.
	/// The reason code is what the player is shown when it is not.
	/// </summary>
	public static bool IsLegal(
		PlayerBetState player,
		PokerActionKind kind,
		int total,
		int currentBet,
		int minRaiseIncrement,
		out string reason)
	{
		if (player == null || player.HasFolded)
		{
			reason = "not_in_hand";
			return false;
		}

		if (!player.CanAct)
		{
			reason = "no_chips";
			return false;
		}

		foreach (var option in LegalActions(player, currentBet, minRaiseIncrement))
		{
			if (option.Kind != kind)
				continue;

			if (option.Allows(total))
			{
				reason = null;
				return true;
			}

			reason = "amount_out_of_range";
			return false;
		}

		reason = "action_not_available";
		return false;
	}

	/// <summary>
	/// The raise sizes A/D steps through, ascending: the minimum, half pot, pot, and all-in.
	///
	/// With no panel and no slider, this IS how a raise gets sized in the common case — clicking
	/// individual chips off the tray is the precision path, not the main one. Everything is clamped
	/// into the legal range and de-duplicated, so a short stack simply offers fewer stops rather
	/// than offering one the server would refuse.
	/// </summary>
	public static List<int> RaisePresets(
		PlayerBetState player, int currentBet, int minRaiseIncrement, int potSize)
	{
		var presets = new List<int>();
		if (player == null || !player.CanAct)
			return presets;

		var max = MaxTotal(player);
		if (max <= currentBet)
			return presets;

		var min = MinRaiseTotal(player, currentBet, minRaiseIncrement);
		var toCall = AmountToCall(player, currentBet);

		void Add(int total)
		{
			var clamped = System.Math.Clamp(total, min, max);
			if (!presets.Contains(clamped))
				presets.Add(clamped);
		}

		Add(min);
		Add(currentBet + (potSize + toCall) / 2);
		Add(currentBet + potSize + toCall);
		Add(max);

		presets.Sort();
		return presets;
	}

	/// <summary>
	/// Whether the street is finished: everyone still holding cards has either run out of chips or
	/// had a turn and matched the bet. A hand down to one player is over regardless.
	/// </summary>
	public static bool RoundIsComplete(IEnumerable<PlayerBetState> players, int currentBet)
	{
		if (players == null)
			return true;

		var live = 0;
		var ready = true;

		foreach (var player in players)
		{
			if (player.HasFolded)
				continue;

			live++;

			if (player.Stack <= 0)
				continue;

			if (!player.HasActedThisRound || player.CommittedThisRound < currentBet)
				ready = false;
		}

		return live <= 1 || ready;
	}

	/// <summary>How many players can still make a decision — under two and the cards just run out.</summary>
	public static int CountAbleToAct(IEnumerable<PlayerBetState> players)
	{
		if (players == null)
			return 0;

		var count = 0;
		foreach (var player in players)
		{
			if (player.CanAct)
				count++;
		}

		return count;
	}

	public static int CountLive(IEnumerable<PlayerBetState> players)
	{
		if (players == null)
			return 0;

		var count = 0;
		foreach (var player in players)
		{
			if (!player.HasFolded)
				count++;
		}

		return count;
	}
}
