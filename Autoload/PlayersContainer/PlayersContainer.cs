using Godot;

[GlobalClass]
public partial class PlayersContainer : Node3D
{
    public override void _Ready()
    {
        PlayerRegistry.Instance.PlayersContainer = this;
    }
}
