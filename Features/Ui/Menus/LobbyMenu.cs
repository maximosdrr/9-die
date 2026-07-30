using Godot;
using Godot.Collections;

[GlobalClass]
public partial class LobbyMenu : Control
{
    private Button _hostButton;
    private Button _refreshButton;
    private Button _joinSessionLocal;
    private Control _lobbyListContainer;

    public override void _Ready()
    {
        _hostButton = GetNode<Button>("VBoxContainer/HBoxContainer/HostButton");
        _refreshButton = GetNode<Button>("VBoxContainer/HBoxContainer/RefreshListButton");
        _joinSessionLocal = GetNode<Button>("VBoxContainer/HBoxContainer/JoinSessionLocal");
        _lobbyListContainer = GetNode<Control>("VBoxContainer/HBoxContainer/ScrollContainer/LobbyList");

        _hostButton.Pressed += OnHostPressed;
        _refreshButton.Pressed += OnRefreshPressed;
        _joinSessionLocal.Pressed += JoinSessionLocal;

        NetworkManager.Instance.NetworkProvider.LobbyCreated += OnLobbyCreated;
        NetworkManager.Instance.NetworkProvider.LobbySessionJoined += OnLobbySessionJoined;
        NetworkManager.Instance.NetworkProvider.ConnectionFailed += OnError;
        NetworkManager.Instance.NetworkProvider.LobbyListReceived += OnLobbyListReceived;
    }

    private void OnHostPressed()
    {
        NetworkManager.Instance.CreateHostSession();
        _hostButton.Disabled = true;
    }

    private void OnRefreshPressed()
    {
        NetworkManager.Instance.RefreshLobbyList();
    }

    private void JoinSessionLocal()
    {
        NetworkManager.Instance.JoinSession();
        _joinSessionLocal.Disabled = true;
    }

    private async void OnLobbySessionJoined(int lobbyId, int guestPeerId, int hostId)
    {
        await ToSignal(NetworkManager.Instance.NetworkProvider, NetworkProvider.SignalName.PlayerConnected);
        Visible = false;
    }

    private async void OnLobbyCreated(int lobbyId, int hostPeerId)
    {
        await ToSignal(NetworkManager.Instance.NetworkProvider, NetworkProvider.SignalName.PlayerConnected);
        Visible = false;
    }

    private void OnError(string msg)
    {
        GD.Print("Error: ", msg);
        _hostButton.Disabled = false;
        _joinSessionLocal.Disabled = false;
    }

    private void OnLobbyListReceived(Array lobbies)
    {
        foreach (var child in _lobbyListContainer.GetChildren())
            child.QueueFree();

        foreach (var lobbyVariant in lobbies)
        {
            var lobbyId = 0;
            var lobbyName = "Unknown Lobby";

            if (lobbyVariant.VariantType == Variant.Type.Int)
            {
                lobbyId = lobbyVariant.AsInt32();
                lobbyName = "Room " + lobbyId;
            }
            else if (lobbyVariant.VariantType == Variant.Type.Dictionary)
            {
                var lobbyDict = lobbyVariant.AsGodotDictionary();
                lobbyId = lobbyDict.TryGetValue("id", out var idValue) ? idValue.AsInt32() : 0;
                lobbyName = lobbyDict.TryGetValue("name", out var nameValue) ? nameValue.AsString() : "Room";
            }

            var btn = new Button();
            btn.Text = $"{lobbyName} (ID: {lobbyId})";
            btn.Alignment = HorizontalAlignment.Left;

            var capturedLobbyId = lobbyId;
            btn.Pressed += () => OnLobbyItemPressed(capturedLobbyId);
            _lobbyListContainer.AddChild(btn);
        }
    }

    private void OnLobbyItemPressed(int lobbyId)
    {
        GD.Print("Trying enter in lobby: ", lobbyId);
        NetworkManager.Instance.JoinSession(lobbyId);
        Visible = false;
    }
}
