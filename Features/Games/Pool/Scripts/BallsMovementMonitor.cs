using Godot;
using Godot.Collections;

[GlobalClass]
public partial class BallsMovementMonitor : Node
{
    [Export] public float StopTolerance = 0.5f;

    public PoolGame PoolGame;
    public Array<Ball> Balls = new();

    private int _movingCount = 0;
    private ulong _stopWaitToken = 0;
    public bool IsMovingState = false;

    [Signal] public delegate void BallsStoppedEventHandler();
    [Signal] public delegate void BallsMovingEventHandler();

    public void Setup(PoolGame poolGame)
    {
        PoolGame = poolGame;
        Balls = new Array<Ball>(poolGame.Balls);
        Balls.Add(poolGame.CueBall);

        _movingCount = 0;
        IsMovingState = false;

        foreach (var ball in Balls)
        {
            SignalUtil.ConnectGuarded(ball, Ball.SignalName.StartedMoving, new Callable(this, MethodName.OnBallStartedMoving));
            SignalUtil.ConnectGuarded(ball, Ball.SignalName.StoppedMoving, new Callable(this, MethodName.OnBallStoppedMoving));
        }
    }

    private void OnBallStartedMoving()
    {
        _movingCount++;
        _stopWaitToken++;

        if (!IsMovingState)
        {
            IsMovingState = true;
            EmitSignal(SignalName.BallsMoving);
        }
    }

    private async void OnBallStoppedMoving(Vector3 position)
    {
        _movingCount = Mathf.Max(0, _movingCount - 1);

        if (_movingCount > 0)
            return;

        var myToken = ++_stopWaitToken;
        await ToSignal(GetTree().CreateTimer(StopTolerance), SceneTreeTimer.SignalName.Timeout);

        if (myToken != _stopWaitToken || _movingCount > 0 || !IsMovingState)
            return;

        IsMovingState = false;
        EmitSignal(SignalName.BallsStopped);
    }
}
