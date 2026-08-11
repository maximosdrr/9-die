using Godot;

[GlobalClass]
public partial class GoldenNineTurnRuler : TurnRuler
{
    public GoldenNineTurnRuler()
    {
        Type = RulerType.GoldenNine;
    }

    public override Actions Rule(TurnContext context)
    {
        var ballsScored = context.BallsScored;
        var firstBallTouched = context.FirstBallTouched;
        var ballsOffTable = context.BallsOffTable;
        var targetBall = context.TargetBall;

        if (context.IsBreakShot && !context.IsLegalBreak)
            return Actions.CallCueBallReplacement;

        if (ballsScored.ContainsKey(0))
            return Actions.CallCueBallReplacement;

        if (ballsOffTable != null && ballsOffTable.Count > 0)
        {
            return Actions.CallCueBallReplacement;
        }

        // A declared push-out suspends wrong-ball-first and no-rail-after-contact. Scratches and
        // balls driven off remain fouls and were handled above.
        if (context.IsPushOut)
            return Actions.CallPushOutChoice;

        if (firstBallTouched == null)
            return Actions.CallCueBallReplacement;

        // No target left means the rack is exhausted; dereferencing it here used to throw.
        if (targetBall == null)
            return ballsScored.ContainsKey(9) ? Actions.EndGamePlayerWin : Actions.CallNextTurn;

        if (targetBall.Index != firstBallTouched.Index)
            return Actions.CallCueBallReplacement;

        // Under current WPA nine-ball rules the 9 made on the break is spotted and the breaker
        // continues; it is not an instant win. The resolver performs the re-spot.
        if (ballsScored.ContainsKey(9) && context.IsBreakShot)
            return Actions.ExtendTurn;

        if (ballsScored.ContainsKey(9))
            return Actions.EndGamePlayerWin;

        if (ballsScored.Count > 0)
            return Actions.ExtendTurn;

        if (!context.AnyRailContact)
            return Actions.CallCueBallReplacement;

        return Actions.CallNextTurn;
    }
}
