using System.Collections.Generic;

/// <summary>
/// Shared timing contract between the server-side hand lifecycle and the client presentation.
/// Defaults live here so extending an animation cannot silently make the next hand erase it early.
/// A configured pause of zero in the resolver remains an explicit instant/headless mode.
/// </summary>
public static class PokerPresentationTiming
{
	private static readonly PokerPresentationProfile Defaults = new();
	public static float ChipFlightSeconds => Defaults.ChipFlightSeconds;
	public static float ChipFlightStagger => Defaults.ChipFlightStagger;
	public static float ChipLandingSeconds => Defaults.ChipLandingSeconds;
	public static float ChipCollectSeconds => Defaults.ChipCollectSeconds;
	public static float ChipOrganizeSeconds => Defaults.ChipOrganizeSeconds;
	public static float ChipCollectStagger => Defaults.ChipCollectStagger;
	public static float ChipPayoutSeconds => Defaults.ChipPayoutSeconds;
	public static float ChipPayoutStagger => Defaults.ChipPayoutStagger;
	public static float DealerChangeSeconds => Defaults.DealerChangeSeconds;
	public static float DealerPayoutSeconds => Defaults.DealerPayoutSeconds;
	public static float ShowdownRevealHoldSeconds => Defaults.ShowdownRevealHoldSeconds;
	public static float ShowdownCardSeconds => Defaults.ShowdownCardSeconds;
	public static float ShowdownRowStagger => Defaults.ShowdownRowStagger;
	public static float ShowdownCardStagger => Defaults.ShowdownCardStagger;
	public static float RankedHandsReadingSeconds => Defaults.RankedHandsReadingSeconds;
	public static float TransitionSafetySeconds => Defaults.TransitionSafetySeconds;
	public static int DefaultMaxAnimatedChipGroups => Defaults.MaxAnimatedChipGroups;

	public static int EstimateChipGroups(
		IEnumerable<int> contributions, int maximum = -1)
		=> Defaults.EstimateChipGroups(contributions, maximum);

	/// <summary>Minimum safe time before a completed hand may be replaced by the next deal.</summary>
	public static float MinimumHandPause(
		bool showdown, int chipGroups, int revealedPlayers, int winnerCount)
		=> Defaults.MinimumHandPause(showdown, chipGroups, revealedPlayers, winnerCount);
}
