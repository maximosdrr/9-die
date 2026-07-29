using Godot;
using Godot.Collections;

[GlobalClass]
public partial class CueSpinState : State
{
    private const string InputSpinModifier = "spin_modifier";
    [Export] public float SpinSensitivity = 0.001f;

    public Cue Cue;

    public CueSpinState()
    {
        Type = StatesRef.CueSpinning;
    }

    public override void Setup(Node3D parentNode)
    {
        Cue = parentNode as Cue;
    }

    public override void HandleInput(InputEvent @event)
    {
        if (!Cue.IsMultiplayerAuthority())
            return;

        if (@event.IsActionReleased(InputSpinModifier))
        {
            StateMachine.ChangeState(StatesRef.CueIdle, new Dictionary());
            Cue.GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            ProcessSpinInput(motion.Relative);
            UpdateCuePose();
            Cue.GetViewport().SetInputAsHandled();
        }
    }

    public override void Process(double delta)
    {
        UpdateCuePose();
    }

    private void ProcessSpinInput(Vector2 relativeMotion)
    {
        var motionDelta = relativeMotion * SpinSensitivity;

        var spin = Cue.SpinOffset;
        spin.X += motionDelta.X;
        spin.Y -= motionDelta.Y;
        Cue.SpinOffset = spin.LimitLength(Cue.SpinLimit);
    }

    private void UpdateCuePose()
    {
        var pos = Cue.Position;
        pos.X = Cue.SpinOffset.X;
        pos.Y = Cue.SpinOffset.Y;
        pos.Z = Cue.BallRadiusOffset;
        Cue.Position = pos;
    }
}
