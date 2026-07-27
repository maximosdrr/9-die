using Godot;
using Godot.Collections;

[GlobalClass]
public partial class BallsMovementMonitor : Node
{
    [Export] public float BallCheckDelay = 0.1f;
    [Export] public float StopTolerance = 0.5f;

    public PoolGame PoolGame;
    public Array<Ball> Balls = new();

    private float _ballCheckTimer = 0.0f;
    private float _stopToleranceTimer = 0.0f;
    public bool IsMovingState = false;

    [Signal] public delegate void BallsStoppedEventHandler();
    [Signal] public delegate void BallsMovingEventHandler();

    public void Setup(PoolGame poolGame)
    {
        PoolGame = poolGame;
        Balls = new Array<Ball>(poolGame.Balls);
        Balls.Add(poolGame.CueBall);
    }

    public override void _Process(double delta)
    {
        if (_ballCheckTimer > 0)
        {
            _ballCheckTimer -= (float)delta;
            return;
        }

        _ballCheckTimer = BallCheckDelay;

        var anyBallMoving = false;

        foreach (var ball in Balls)
        {
            if (!IsInstanceValid(ball))
                continue;

            if (ball.LinearVelocity.Length() > 0.01f)
            {
                anyBallMoving = true;
                break;
            }
        }

        if (anyBallMoving)
        {
            _stopToleranceTimer = StopTolerance;

            if (!IsMovingState)
            {
                IsMovingState = true;
                EmitSignal(SignalName.BallsMoving);
            }
        }
        else
        {
            if (IsMovingState)
            {
                _stopToleranceTimer -= BallCheckDelay;

                if (_stopToleranceTimer <= 0)
                {
                    IsMovingState = false;
                    EmitSignal(SignalName.BallsStopped);
                }
            }
        }
    }
}
