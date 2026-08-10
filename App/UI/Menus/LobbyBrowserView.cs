using Godot;

/// <summary>Typed contract for the lobby browser; its container hierarchy may change freely.</summary>
[GlobalClass]
public partial class LobbyBrowserView : Control
{
    public Button BackButton { get; private set; }
    public Button RefreshButton { get; private set; }
    public LineEdit SearchEdit { get; private set; }
    public Control LobbyListContainer { get; private set; }
    public Label NoMatchLabel { get; private set; }

    public override void _Ready()
    {
        BackButton = GetNode<Button>("%BackButton");
        RefreshButton = GetNode<Button>("%RefreshButton");
        SearchEdit = GetNode<LineEdit>("%SearchEdit");
        LobbyListContainer = GetNode<Control>("%LobbyList");
        NoMatchLabel = GetNode<Label>("%NoMatchLabel");
    }
}
