using Godot;

public partial class Global : Node
{
    public static Global Instance { get; private set; }

    public string LocalNickname = "";

    public override void _EnterTree()
    {
        Instance = this;
    }
}
