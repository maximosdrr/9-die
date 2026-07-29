using Godot;

[GlobalClass]
public partial class PoolScoreMonitor : Area3D
{
    private void OnBodyEntered(Node3D body)
    {
        if (body is Ball ball)
            StopBall(ball);
    }

    private void StopBall(Ball ball)
    {
        ball.LinearVelocity = Vector3.Zero;
        ball.AngularVelocity = Vector3.Zero;
        ball.Rotation = Vector3.Zero;
    }
}
