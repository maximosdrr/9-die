using Godot;

[GlobalClass]
public partial class GoldenNineBallFellOffListener : Node
{
    public GoldenNineTurnResolver TurnResolver;

    public void Setup(GoldenNineTurnResolver turnResolver)
    {
        TurnResolver = turnResolver;

        SignalUtil.ConnectGuarded(
            TurnResolver.PoolGame.OffTableMonitor,
            OffTableMonitor.SignalName.BallFellOff,
            new Callable(this, MethodName.OnBallFellOff));
    }

    private void OnBallFellOff(Ball ball)
    {
        if (!TurnResolver.BallsOffTableList.Contains(ball))
            TurnResolver.BallsOffTableList.Add(ball);
    }
}
