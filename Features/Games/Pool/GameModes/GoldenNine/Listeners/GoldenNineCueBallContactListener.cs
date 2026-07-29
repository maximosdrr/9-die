using Godot;

[GlobalClass]
public partial class GoldenNineCueBallContactListener : Node
{
    public GoldenNineTurnResolver TurnResolver;

    public void Setup(GoldenNineTurnResolver turnResolver)
    {
        TurnResolver = turnResolver;
    }

    private void OnCueBallContact(Ball ball)
    {
        if (TurnResolver.FirstBallHit == null)
            TurnResolver.FirstBallHit = ball;
    }

    public void StartListeningCollisions()
    {
        SignalUtil.ConnectGuarded(TurnResolver.CueBall, Ball.SignalName.BallContacted, new Callable(this, MethodName.OnCueBallContact));
    }

    public void StopListeningCollisions()
    {
        SignalUtil.DisconnectGuarded(TurnResolver.CueBall, Ball.SignalName.BallContacted, new Callable(this, MethodName.OnCueBallContact));
    }
}
