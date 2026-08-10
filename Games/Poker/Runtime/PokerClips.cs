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
/// The third-person clips are NOT authored yet. The character rig carries only "Idle" and "Walk",
/// and both play paths use HasAnimation, so today every body gesture quietly resolves to the seated
/// idle. When the clips arrive, they drop into this table and nothing else changes — no state, no
/// rule, no RPC.
///
/// Nothing about a gesture travels over the network. Every peer already knows who did what from the
/// turn context's last action, so each one arrives at the same table on its own.
/// </summary>
public static class PokerClips
{
    // ---------------------------------------------------------------- first person

    /// <summary>Holding the cards, doing nothing.</summary>
    public const string Idle = "HandIdle";

    public const string PickUpCards = "HandPickUpCards";
    public const string ThrowChips = "HandThrowChips";
    public const string Knock = "HandKnock";

    /// <summary>Throwing the pair away: forward, down, open. The cards are LET GO of, not lowered.</summary>
    public const string Fold = "HandFold";

    public const string RevealCards = "HandRevealCards";

    // ---------------------------------------------------------------- third person

    /// <summary>What a seated body falls back to. The only clip the character rig actually has.</summary>
    public const string BodyIdle = "Idle";

    public const string BodyPickUpCards = "SitPickUpCards";
    public const string BodyThrowChips = "SitThrowChips";
    public const string BodyKnock = "SitKnock";
    public const string BodyFold = "SitFold";
    public const string BodyReveal = "SitReveal";

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
            "showdown" => PokerGesture.Reveal,
            _ => PokerGesture.None,
        };
}
