using Godot;

[GlobalClass]
public partial class OffTableMonitor : Node
{
    public Area3D BallOffMonitor;

    [Signal]
    public delegate void BallFellOffEventHandler(Ball ball);

    public void Setup(Area3D ballOffMonitor)
    {
        BallOffMonitor = ballOffMonitor;
        BallOffMonitor.BodyEntered += OnBodyEntered;
    }

    private void OnBodyEntered(Node body)
    {
        if (body is Ball ball)
            EmitSignal(SignalName.BallFellOff, ball);
    }
}
