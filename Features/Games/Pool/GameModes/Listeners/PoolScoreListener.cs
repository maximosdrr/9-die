using Godot;

[GlobalClass]
public partial class PoolScoreListener : Node
{
    public PoolTurnResolver TurnResolver;

    public void Setup(PoolTurnResolver turnResolver)
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
