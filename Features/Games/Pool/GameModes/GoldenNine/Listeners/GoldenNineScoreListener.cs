using Godot;

[GlobalClass]
public partial class GoldenNineScoreListener : Node
{
    public GoldenNineTurnResolver TurnResolver;

    public void Setup(GoldenNineTurnResolver turnResolver)
    {
        TurnResolver = turnResolver;

        SignalUtil.ConnectGuarded(
            TurnResolver.PoolGame.ScoreMonitor,
            Area3D.SignalName.BodyEntered,
            new Callable(this, MethodName.OnBallTouchScoreGround));
    }

    private void OnBallTouchScoreGround(Node3D body)
    {
        if (body is Ball ball)
            TurnResolver.BallsScored[ball.Index] = ball;
    }
}
