using Godot;

/// <summary>Typed contract for the home screen; layout containers stay private to the scene.</summary>
[GlobalClass]
public partial class HomeMenuView : Control
{
    public LineEdit NicknameEdit { get; private set; }
    public Button ShuffleButton { get; private set; }
    public Control IpField { get; private set; }
    public LineEdit IpEdit { get; private set; }
    public Button HostButton { get; private set; }
    public Button JoinButton { get; private set; }
    public Label HintLabel { get; private set; }

    public override void _Ready()
    {
        NicknameEdit = GetNode<LineEdit>("%NicknameEdit");
        ShuffleButton = GetNode<Button>("%ShuffleButton");
        IpField = GetNode<Control>("%IpField");
        IpEdit = GetNode<LineEdit>("%IpEdit");
        HostButton = GetNode<Button>("%HostButton");
        JoinButton = GetNode<Button>("%JoinButton");
        HintLabel = GetNode<Label>("%HintLabel");
    }
}
