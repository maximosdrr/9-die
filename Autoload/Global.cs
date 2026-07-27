using Godot;

public partial class Global : Node
{
    public static Global Instance { get; private set; }

    public GlobalCamera Camera;

    public override void _Ready()
    {
        Instance = this;
    }
}
