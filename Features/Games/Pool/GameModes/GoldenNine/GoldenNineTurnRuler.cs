using Godot;
using Godot.Collections;
using System.Linq;

[GlobalClass]
public partial class GoldenNineTurnRuler : TurnRuler
{
    public GoldenNineTurnRuler()
    {
        Type = RulerType.GoldenNine;
    }

    public override Actions Rule(Dictionary context)
    {
        var ballsScored = (Dictionary)context["balls_scored"];
        var firstBallTouched = context["first_ball_touched"].As<Ball>();
        var ballsOffTable = (Array<Ball>)context["balls_off_table"];
        var targetBall = context["target_ball"].As<Ball>();

        if (ballsScored.ContainsKey(0))
        {
            if (ballsScored.ContainsKey(9))
                return Actions.EndGameFatalFoul;
            return Actions.CallCueBallReplacement;
        }

        if (firstBallTouched == null)
            return Actions.CallCueBallReplacement;

        if (ballsOffTable != null && ballsOffTable.Count > 0)
        {
            var hasGoldenBall = ballsOffTable.Any(ball => ball.Index == 9);

            if (hasGoldenBall)
                return Actions.EndGameFatalFoul;

            return Actions.CallCueBallReplacement;
        }

        if (targetBall.Index != firstBallTouched.Index)
        {
            if (ballsScored.ContainsKey(9))
                return Actions.EndGameFatalFoul;

            return Actions.CallCueBallReplacement;
        }

        if (ballsScored.ContainsKey(9))
            return Actions.EndGamePlayerWin;

        if (ballsScored.Count > 0)
            return Actions.ExtendTurn;

        return Actions.CallNextTurn;
    }
}
