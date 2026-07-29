using Godot;

public partial class Global : Node
{
    public static Global Instance { get; private set; }

    public GlobalCamera Camera;
    public TvScreenShare TvScreen;

    public override void _EnterTree()
    {
        Instance = this;
    }
}
