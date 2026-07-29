using Godot;

[GlobalClass]
public partial class BallIdleState : State
{
    public float StopSpeedThreshold = 0.01f;
    public float StopCheckTimerValue = 0.1f;
    public float StopCheckTimer;

    [Export] public Ball Ball;

    public BallIdleState()
    {
        Type = StatesRef.BallIdle;
        StopCheckTimer = StopCheckTimerValue;
    }

    public override void PhysicsProcess(double delta)
    {
        StopCheckTimer -= (float)delta;

        if (StopCheckTimer > 0)
            return;

        StopCheckTimer = StopCheckTimerValue;

        if (Ball.LinearVelocity.Length() > StopSpeedThreshold)
            StateMachine.ChangeState(StatesRef.BallMoving, new Godot.Collections.Dictionary());
    }
}
