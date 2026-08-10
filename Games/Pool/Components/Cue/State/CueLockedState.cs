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
        Cue.SnapToRestPose();
    }

    public override void Process(double delta)
    {
        Cue.SnapToRestPose();
    }
}
