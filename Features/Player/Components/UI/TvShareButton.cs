using Godot;

[GlobalClass]
public partial class TvShareButton : CanvasLayer
{
    private Button _button;
    private Label _statusLabel;
    private bool _isSharing = false;

    public override void _Ready()
    {
        _button = GetNode<Button>("Panel/VBoxContainer/ShareButton");
        _statusLabel = GetNode<Label>("Panel/VBoxContainer/StatusLabel");

        if (!IsMultiplayerAuthority() || OS.GetName() != "Windows")
        {
            Visible = false;
            SetProcess(false);
            return;
        }

        _button.Pressed += OnButtonPressed;

        Global.Instance.TvScreen.SharerChanged += OnSharerChanged;
        UpdateUi(Global.Instance.TvScreen.SharerId);
    }

    public override void _ExitTree()
    {
        if (Global.Instance?.TvScreen != null)
            Global.Instance.TvScreen.SharerChanged -= OnSharerChanged;
    }

    private void OnButtonPressed()
    {
        if (_isSharing)
            Global.Instance.TvScreen.RequestStopSharing();
        else
            Global.Instance.TvScreen.RequestStartSharing();
    }

    private void OnSharerChanged(int sharerId)
    {
        UpdateUi(sharerId);
    }

    private void UpdateUi(int sharerId)
    {
        var myId = Multiplayer.GetUniqueId();
        _isSharing = sharerId == myId;

        if (_isSharing)
        {
            _button.Text = "Parar de Compartilhar";
            _button.Disabled = false;
            _statusLabel.Text = "🔴 Compartilhando sua tela";
            _statusLabel.Visible = true;
        }
        else if (sharerId != 0)
        {
            _button.Text = "Compartilhar Tela";
            _button.Disabled = true;
            _statusLabel.Text = "Outro jogador está compartilhando";
            _statusLabel.Visible = true;
        }
        else
        {
            _button.Text = "Compartilhar Tela";
            _button.Disabled = false;
            _statusLabel.Visible = false;
        }
    }
}
