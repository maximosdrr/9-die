using System.Collections.Generic;
using System.Linq;
using Godot;
using Poker.Rules;

/// <summary>
/// Single tunable contract for every timed poker presentation. The same resource is assigned to
/// the server resolver and the client presenters, so extending a visual beat also extends the
/// minimum interval before the authoritative next hand may replace it.
/// </summary>
[GlobalClass]
public partial class PokerPresentationProfile : Resource
{
    [ExportGroup("Pot collection")]
    [Export] public float ChipFlightSeconds { get; set; } = 0.68f;
    [Export] public float ChipFlightStagger { get; set; } = 0.025f;
    [Export] public float ChipLandingSeconds { get; set; } = 0.28f;
    [Export] public float ChipCollectSeconds { get; set; } = 0.62f;
    [Export] public float ChipOrganizeSeconds { get; set; } = 0.46f;
    [Export] public float ChipCollectStagger { get; set; } = 0.08f;

    [ExportGroup("Payout")]
    [Export] public float ChipPayoutSeconds { get; set; } = 0.82f;
    [Export] public float ChipPayoutStagger { get; set; } = 0.045f;
    [Export] public float WinnerLooseHoldSeconds { get; set; } = 0.24f;
    [Export] public float WinnerOrganizeSeconds { get; set; } = 0.58f;
    [Export] public float WinnerOrganizeStagger { get; set; } = 0.025f;
    [Export] public float DealerChangeSeconds { get; set; } = 1.15f;
    [Export] public float DealerPayoutSeconds { get; set; } = 1.15f;
    [Export] public float DealerChangeStagger { get; set; } = 0.020f;

    [ExportGroup("Showdown")]
    /// <summary>Time from Showdown starting until the authored hand releases the cards.</summary>
    [Export] public float ShowdownReleaseDelaySeconds { get; set; }
        = PokerClips.ShowdownReleaseSeconds;
    [Export] public float ShowdownRevealMotionSeconds { get; set; } = 0.68f;
    [Export] public float ShowdownRevealHoldSeconds { get; set; } = 2.5f;
    [Export] public float ShowdownCardSeconds { get; set; } = 0.72f;
    [Export] public float ShowdownRowStagger { get; set; } = 0.12f;
    [Export] public float ShowdownCardStagger { get; set; } = 0.035f;
    [Export] public float RankedHandsReadingSeconds { get; set; } = 8.0f;

    [ExportGroup("Next hand")]
    [Export] public float CardReturnSeconds { get; set; } = 1.15f;
    [Export] public float CardReturnStagger { get; set; } = 0.075f;
    [Export] public float DeckGatherHoldSeconds { get; set; } = 0.32f;
    [Export] public float DeckShuffleSeconds { get; set; } = 1.90f;
    [Export(PropertyHint.Range, "13,24,1")] public int MaximumCollectionCardSlots { get; set; } = 20;

    [ExportGroup("Limits")]
    [Export] public float TransitionSafetySeconds { get; set; } = 0.35f;
    [Export] public int MaxAnimatedChipGroups { get; set; } = 80;

    public float CardCleanupDuration => CardCleanupDurationFor(MaximumCollectionCardSlots);

    public float CardCleanupDurationFor(int cardSlots) => Mathf.Max(0.0f, CardReturnSeconds)
        + Mathf.Max(0, cardSlots - 1) * Mathf.Max(0.0f, CardReturnStagger)
        + Mathf.Max(0.0f, DeckGatherHoldSeconds)
        + Mathf.Max(0.0f, DeckShuffleSeconds);

    public int EstimateChipGroups(IEnumerable<int> contributions, int maximum = -1)
    {
        if (contributions == null)
            return 0;

        var physical = contributions.Where(value => value > 0)
            .Sum(value => PokerChipStack.ChipCount(PokerChipStack.Decompose(value)));
        var cap = maximum > 0 ? maximum : MaxAnimatedChipGroups;
        return Mathf.Min(Mathf.Max(1, cap), physical);
    }

    /// <summary>Minimum safe time before a completed hand may be replaced by the next deal.</summary>
    public float MinimumHandPause(
        bool showdown, int chipGroups, int revealedPlayers, int winnerCount,
        int cleanupCardSlots = -1)
    {
        var groups = Mathf.Max(0, chipGroups);
        var finalBet = groups == 0 ? 0.0f
            : ChipFlightSeconds + ChipLandingSeconds
              + Mathf.Max(0, groups - 1) * ChipFlightStagger;
        var collection = groups == 0 ? 0.0f
            : ChipCollectSeconds + Mathf.Max(0, groups - 1) * ChipCollectStagger
              + ChipOrganizeSeconds;
        var payout = groups == 0 ? 0.0f
            : ChipPayoutSeconds + Mathf.Max(0, groups - 1) * ChipPayoutStagger
              + WinnerOrganizeSeconds
              + WinnerLooseHoldSeconds
              + Mathf.Max(0, groups - 1) * WinnerOrganizeStagger
              + Mathf.Max(0, winnerCount - 1) * ShowdownRowStagger;
        if (winnerCount > 1 && groups > 0)
            payout += DealerChangeSeconds + Mathf.Max(0, groups - 1) * DealerChangeStagger
                + Mathf.Max(0.0f, DealerPayoutSeconds - ChipPayoutSeconds);

        if (!showdown)
            return finalBet + collection + payout
                + CardCleanupDurationFor(cleanupCardSlots > 0
                    ? cleanupCardSlots : MaximumCollectionCardSlots)
                + TransitionSafetySeconds;

        var ranking = ShowdownCardSeconds
            + Mathf.Max(0, revealedPlayers - 1) * ShowdownRowStagger
            + 4 * ShowdownCardStagger;
        return finalBet + collection + ShowdownReleaseDelaySeconds
            + ShowdownRevealMotionSeconds
            + ShowdownRevealHoldSeconds + ranking
            + RankedHandsReadingSeconds + payout
            + CardCleanupDurationFor(cleanupCardSlots > 0
                ? cleanupCardSlots : MaximumCollectionCardSlots)
            + TransitionSafetySeconds;
    }
}
