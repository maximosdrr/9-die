using Godot;
using System.Threading.Tasks;

[GlobalClass]
public partial class NotificationPopup : PanelContainer
{
    [Signal]
    public delegate void ResponseReceivedEventHandler(bool accepted);

    private Label _label;
    private Button _buttonAccept;
    private Button _buttonReject;

    public override void _Ready()
    {
        _label = GetNode<Label>("VBoxContainer/Label");
        _buttonAccept = GetNode<Button>("VBoxContainer/HBoxContainer/ButtonAccept");
        _buttonReject = GetNode<Button>("VBoxContainer/HBoxContainer/ButtonReject");

        _buttonAccept.Pressed += () => OnResponse(true);
        _buttonReject.Pressed += () => OnResponse(false);
        Hide();
    }

    public async Task<bool> RequestDecision(string message)
    {
        _label.Text = message;
        Show();

        var result = await ToSignal(this, SignalName.ResponseReceived);

        Hide();
        return (bool)result[0];
    }

    private void OnResponse(bool accepted)
    {
        EmitSignal(SignalName.ResponseReceived, accepted);
    }
}
