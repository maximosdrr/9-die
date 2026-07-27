using Godot;
using Godot.Collections;

[GlobalClass]
public partial class CueLockedState : State
{
    public Cue Cue;

    public CueLockedState()
    {
        Type = StatesRef.CueLocked;
    }

    public override void Setup(Node3D parentNode)
    {
        Cue = parentNode as Cue;
    }

    public override void Enter(Dictionary metadata)
    {
        var pos = Cue.Position;
        pos.Z = Cue.BallRadiusOffset;
        Cue.Position = pos;
        ApplySpinToPose();
    }

    public override void Process(double delta)
    {
        var pos = Cue.Position;
        pos.Z = Cue.BallRadiusOffset;
        Cue.Position = pos;
        ApplySpinToPose();
    }

    private void ApplySpinToPose()
    {
        var pos = Cue.Position;
        pos.X = Cue.SpinOffset.X;
        pos.Y = Cue.SpinOffset.Y;
        Cue.Position = pos;
    }
}
