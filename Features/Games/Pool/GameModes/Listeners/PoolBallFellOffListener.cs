using Godot;

[GlobalClass]
public partial class PoolBallFellOffListener : Node
{
    public PoolTurnResolver TurnResolver;

    public void Setup(PoolTurnResolver turnResolver)
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
