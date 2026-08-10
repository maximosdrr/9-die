namespace Poker.Rules;

/// <summary>
/// The keys poker answers to, named once.
///
/// R, C, X and Z are what was left: F, E and Q are already interact, switch-control and hold-to-leave,
/// so the usual poker letters were not available. Kept here rather than as literals scattered
/// through the states so the HUD and the states can never disagree about which key does what —
/// the HUD prints these, the states read them.
/// </summary>
public static class PokerInput
{
    public const string Call = "poker_call";
    public const string Raise = "poker_raise";
    public const string Fold = "poker_fold";
    public const string AllIn = "poker_all_in";

    /// <summary>Held, not pressed: the cards stay up only while the button is down.</summary>
    public const string Peek = "peek_cards";

    public const string StepDown = "move_left";
    public const string StepUp = "move_right";

    /// <summary>What each key looks like on the HUD.</summary>
    public const string CallKey = "C";
    public const string RaiseKey = "R";
    public const string FoldKey = "X";
    public const string AllInKey = "Z";
}
