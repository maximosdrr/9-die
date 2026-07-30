using Godot.Collections;

/// <summary>
/// What a TurnRuler needs to decide the outcome of a turn — the physical facts a TurnResolver
/// gathered after the balls stopped moving. Shared across every pool mode (which balls got
/// scored/fell off/were hit first are universal pool concepts), even though only one TurnRuler
/// exists today.
/// </summary>
public sealed class TurnContext
{
    public Dictionary<int, Ball> BallsScored;
    public Ball FirstBallTouched;
    public Array<Ball> BallsOffTable;
    public Ball TargetBall;
    public Dictionary<int, Ball> CurrentBallsRemaining;
    public bool AnyRailContact;
}
