using Godot;
using Godot.Collections;

[GlobalClass]
public partial class CueIdleState : State
{
    private const string InputSpinModifier = "spin_modifier";
    private const string InputStrokeMode = "stroke_mode";
    private const string InputElevationModifier = "elevation_modifier";

    public Cue Cue;

    public CueIdleState()
    {
        Type = StatesRef.CueIdle;
    }

    public override void Setup(Node3D parentNode)
    {
        Cue = parentNode as Cue;
    }

    public override void Enter(Dictionary metadata)
    {
        Cue.SnapToRestPose();
    }

    public override void HandleInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputSpinModifier))
        {
            StateMachine.ChangeState(StatesRef.CueSpinning, new Dictionary());
            Cue.GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed(InputStrokeMode))
        {
            StateMachine.ChangeState(StatesRef.CueCharging, new Dictionary());
            Cue.GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed(InputElevationModifier))
        {
            StateMachine.ChangeState(StatesRef.CueJumping, new Dictionary());
            Cue.GetViewport().SetInputAsHandled();
            return;
        }
    }

    public override void Process(double delta)
    {
        Cue.SnapToRestPose();
    }
}
