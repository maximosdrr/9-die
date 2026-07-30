using Godot;

public partial class PlayerRegistry : Node
{
    public static PlayerRegistry Instance { get; private set; }

    public Node3D PlayersContainer;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public bool HasContainer()
    {
        return IsInstanceValid(PlayersContainer);
    }

    public Player GetPlayerById(string id)
    {
        if (!HasContainer())
        {
            GD.PushError("PlayersContainer not set yet");
            return null;
        }

        foreach (var node in PlayersContainer.GetChildren())
        {
            if (node is not Player player)
                continue;
            if (node.Name != id)
                continue;
            return player;
        }

        GD.PushError("Player not found");
        return null;
    }
}
