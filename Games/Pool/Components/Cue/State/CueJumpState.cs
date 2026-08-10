using Godot;
using Godot.Collections;

[GlobalClass]
public partial class CueJumpState : State
{
    private const string InputElevationModifier = "elevation_modifier";

    public Cue Cue;

    public CueJumpState()
    {
        Type = StatesRef.CueJumping;
    }

    public override void Setup(Node3D parentNode)
    {
        Cue = parentNode as Cue;
    }

    public override void Enter(Dictionary metadata)
    {
        Cue.CurrentElevation = Mathf.RadToDeg(Cue.MinSafeAngle);
    }

    public override void HandleInput(InputEvent @event)
    {
        if (@event.IsActionReleased(InputElevationModifier))
        {
            StateMachine.ChangeState(StatesRef.CueIdle, new Dictionary());
            Cue.GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                AdjustElevation(1);
                Cue.GetViewport().SetInputAsHandled();
                return;
            }

            if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                AdjustElevation(-1);
                Cue.GetViewport().SetInputAsHandled();
                return;
            }
        }
    }

    public override void Process(double delta)
    {
        Cue.SnapToRestPose();
    }

    // Only the target is set here. Cue._Process is the single writer of the node's actual
    // rotation — previously this wrote RotationDegrees.X directly while _Process was easing the
    // same property in the same frame, so the two fought each other.
    private void AdjustElevation(int direction)
    {
        var step = direction * Cue.ElevationSensitivity;
        var safeLimitDeg = Mathf.RadToDeg(Cue.MinSafeAngle);

        Cue.CurrentElevation = Mathf.Clamp(
            Cue.CurrentElevation - step,
            Cue.JumpMaxAngle,
            safeLimitDeg);
    }
}
