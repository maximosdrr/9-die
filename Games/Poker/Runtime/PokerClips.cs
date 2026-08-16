/// <summary>Something a player does at the table that both a hand and a body can perform.</summary>
public enum PokerGesture
{
    None = 0,

    /// <summary>Reaching down and taking the dealt pair off the cloth.</summary>
    PickUpCards = 1,

    /// <summary>Pushing chips in: a bet, a call or a raise.</summary>
    ThrowChips = 2,

    /// <summary>Knocking the table to check.</summary>
    Knock = 3,

    /// <summary>Throwing the cards away.</summary>
    Fold = 4,

    /// <summary>Turning the cards face up at a showdown.</summary>
    Reveal = 5,
}

/// <summary>
/// The animation each gesture plays, in FIRST and THIRD person.
///
/// One vocabulary, two consumers. The local player's rig performs the first-person clip; every
/// other peer performs the third-person one on the seated body. That split is the whole reason this
/// table exists rather than clip names scattered through the states: the two are authored by
/// different people at different times, and a gesture has to be nameable before either exists.
///
/// PickCards, the card-holding idles, PokerBet, PokerPass and Showdown are authored in both views.
/// Fold keeps its replaceable fallback until its dedicated pair is produced.
///
/// Public actions come from the turn context. The private right-button look is the one exception:
/// its raised/lowered edge is replicated separately so other players see the same body pose.
/// </summary>
public static class PokerClips
{
    // ---------------------------------------------------------------- first person

    /// <summary>
    /// Auxiliary motion on the legacy HandRig. Turn-state changes may replay this clip, while the
    /// imported full body remains exclusively controlled by the authored card-pose sequence.
    /// </summary>
    public const string GrossIdle = "HandIdle";

    /// <summary>Holding the cards low, doing nothing.</summary>
    public const string Idle = CharacterVisual.Clips.IdleHoldingCardsDown;

    /// <summary>Cards raised toward the eyes while the player is looking at them.</summary>
    public const string LookCards = CharacterVisual.Clips.IdleSitHoldingCards;

    public const string PickUpCards = CharacterVisual.Clips.PickCards;
    public const string ThrowChips = CharacterVisual.Clips.PokerBet;
    public const string Knock = CharacterVisual.Clips.PokerPass;

    /// <summary>Throwing the pair away: forward, down, open. The cards are LET GO of, not lowered.</summary>
    public const string Fold = "HandFold";

    public const string RevealCards = CharacterVisual.Clips.Showdown;

    // ---------------------------------------------------------------- third person

    /// <summary>Breathing loop with the cards resting low.</summary>
    public const string BodyIdle = CharacterVisual.Clips.IdleHoldingCardsDown;

    public const string BodyLookCards = CharacterVisual.Clips.IdleSitHoldingCards;

    public const string BodyPickUpCards = CharacterVisual.Clips.PickCards;
    public const string BodyThrowChips = CharacterVisual.Clips.PokerBet;
    public const string BodyKnock = CharacterVisual.Clips.PokerPass;
    public const string BodyFold = "SitFold";
    public const string BodyReveal = CharacterVisual.Clips.Showdown;

    /// <summary>Authored timings shared by visual, sound and physical-card presentation.</summary>
    public const float PokerPassDurationSeconds = 1.20f;
    public const float PokerPassFirstContactFraction = 0.50f;
    public const float ShowdownDurationSeconds = 2.50f;
    /// <summary>Down-to-raised settling beat before the reveal clip. Skipped if cards are raised.</summary>
    public const float ShowdownPreparationSeconds = 0.40f;
    public const float ShowdownTransitionBlendSeconds = 0.22f;
    public const float ShowdownReleaseFraction = 0.60f;
    public const float ShowdownReleaseSeconds =
        ShowdownDurationSeconds * ShowdownReleaseFraction;

    /// <summary>The clip this gesture plays on the local player's own hand.</summary>
    public static string FirstPerson(PokerGesture gesture) =>
        gesture switch
        {
            PokerGesture.PickUpCards => PickUpCards,
            PokerGesture.ThrowChips => ThrowChips,
            PokerGesture.Knock => Knock,
            PokerGesture.Fold => Fold,
            PokerGesture.Reveal => RevealCards,
            _ => Idle,
        };

    /// <summary>The clip this gesture plays on a seated body, seen by everyone else.</summary>
    public static string ThirdPerson(PokerGesture gesture) =>
        gesture switch
        {
            PokerGesture.PickUpCards => BodyPickUpCards,
            PokerGesture.ThrowChips => BodyThrowChips,
            PokerGesture.Knock => BodyKnock,
            PokerGesture.Fold => BodyFold,
            PokerGesture.Reveal => BodyReveal,
            _ => BodyIdle,
        };

    /// <summary>
    /// The gesture a server action code stands for. This is what lets every peer replay what
    /// somebody else just did without a single extra message: the code is already in the context.
    /// </summary>
    public static PokerGesture ForAction(string lastAction) =>
        lastAction switch
        {
            "fold" => PokerGesture.Fold,
            "check" => PokerGesture.Knock,
            "call" or "raise" => PokerGesture.ThrowChips,
            _ => PokerGesture.None,
        };
}
