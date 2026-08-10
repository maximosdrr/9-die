using Godot;

[GlobalClass]
public partial class PlayersContainer : Node3D
{
    public override void _Ready()
    {
        PlayerRegistry.Instance?.RegisterContainer(this);
    }

    public override void _ExitTree()
    {
        PlayerRegistry.Instance?.UnregisterContainer(this);
    }
}
