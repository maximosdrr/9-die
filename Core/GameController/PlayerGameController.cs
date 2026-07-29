using Godot;
using Godot.Collections;

[GlobalClass]
public partial class PlayerGameController : Node3D
{
    public bool CanTakeControl = false;

    public virtual void Setup(Player parent, TableGame tableGame) { }

    public override void _Ready() { }

    public override void _Process(double delta) { }

    public virtual void TakeControl() { }

    public virtual void GiveControl() { }

    public virtual void ApplyControl(string turnOwnerId, Dictionary context) { }
}
