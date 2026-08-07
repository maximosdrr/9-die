using Godot;
using System.Linq;

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

        if (ballsScored.ContainsKey(0))
            return Actions.CallCueBallReplacement;

        if (firstBallTouched == null)
            return Actions.CallCueBallReplacement;

        if (ballsOffTable != null && ballsOffTable.Count > 0)
        {
            var hasGoldenBall = ballsOffTable.Any(ball => ball.Index == 9);

            if (hasGoldenBall)
                return Actions.EndGameFatalFoul;

            return Actions.CallCueBallReplacement;
        }

        // No target left means the rack is exhausted; dereferencing it here used to throw.
        if (targetBall == null)
            return ballsScored.ContainsKey(9) ? Actions.EndGamePlayerWin : Actions.CallNextTurn;

        if (targetBall.Index != firstBallTouched.Index)
            return Actions.CallCueBallReplacement;

        if (ballsScored.ContainsKey(9))
            return Actions.EndGamePlayerWin;

        if (ballsScored.Count > 0)
            return Actions.ExtendTurn;

        if (!context.AnyRailContact)
            return Actions.CallCueBallReplacement;

        return Actions.CallNextTurn;
    }
}
