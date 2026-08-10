using Godot;
using Godot.Collections;

[GlobalClass]
public partial class LobbyMenu : Control
{
    private static readonly PackedScene LobbyListItemScene = GD.Load<PackedScene>("res://App/UI/Menus/LobbyListItem.tscn");

    private static readonly string[] RandomNicknames =
    {
        "Taco-Certeiro", "Bola-9-Voadora", "Rei-do-Bar", "Sinuca-Fantasma",
        "Tabela-Dupla", "Mão-de-Ferro", "Giz-Azul", "Caçapa-Facil",
        "Faixa-Verde", "Bicho-do-Feltro", "Tacada-Final", "Mestre-Cueiro",
    };

    private HomeMenuView _homeScreen;
    private LobbyBrowserView _browseScreen;

    private LineEdit _nicknameEdit;
    private Button _shuffleButton;
    private Control _ipField;
    private LineEdit _ipEdit;
    private Button _hostButton;
    private Button _joinButton;
    private Label _hintLabel;

    private Button _backButton;
    private Button _refreshButton;
    private LineEdit _searchEdit;
    private Control _lobbyListContainer;
    private Label _noMatchLabel;

    private Array _lastLobbies = new();

    public override void _Ready()
    {
        _homeScreen = GetNode<HomeMenuView>("HomeScreen");
        _browseScreen = GetNode<LobbyBrowserView>("BrowseScreen");

        _nicknameEdit = _homeScreen.NicknameEdit;
        _shuffleButton = _homeScreen.ShuffleButton;
        _ipField = _homeScreen.IpField;
        _ipEdit = _homeScreen.IpEdit;
        _hostButton = _homeScreen.HostButton;
        _joinButton = _homeScreen.JoinButton;
        _hintLabel = _homeScreen.HintLabel;

        _backButton = _browseScreen.BackButton;
        _refreshButton = _browseScreen.RefreshButton;
        _searchEdit = _browseScreen.SearchEdit;
        _lobbyListContainer = _browseScreen.LobbyListContainer;
        _noMatchLabel = _browseScreen.NoMatchLabel;

        _nicknameEdit.Text = string.IsNullOrWhiteSpace(Global.Instance.LocalNickname)
            ? RandomNicknames[GD.Randi() % RandomNicknames.Length]
            : Global.Instance.LocalNickname;
        Global.Instance.LocalNickname = _nicknameEdit.Text;
        _nicknameEdit.TextChanged += OnNicknameChanged;
        _shuffleButton.Pressed += OnShufflePressed;

        var supportsBrowsing = NetworkManager.Instance.NetworkProvider.SupportsSessionBrowsing;
        _ipField.Visible = !supportsBrowsing;
        _joinButton.Text = supportsBrowsing ? "Buscar Sala" : "Entrar";
        _hintLabel.Text = supportsBrowsing
            ? "Sem conexão direta disponível — busque uma sala aberta."
            : "Digite o IP do host, ou deixe em branco pra testar na mesma máquina (127.0.0.1).";
        _joinButton.Pressed += supportsBrowsing ? OnBrowsePressed : JoinSessionEnet;

        _hostButton.Pressed += OnHostPressed;
        _backButton.Pressed += ShowHomeScreen;
        _refreshButton.Pressed += OnRefreshPressed;
        _searchEdit.TextChanged += OnSearchChanged;

        NetworkManager.Instance.NetworkProvider.LobbyCreated += OnLobbyCreated;
        NetworkManager.Instance.NetworkProvider.LobbySessionJoined += OnLobbySessionJoined;
        NetworkManager.Instance.NetworkProvider.ConnectionFailed += OnError;
        NetworkManager.Instance.NetworkProvider.ServerDisconnected += OnServerDisconnected;
        NetworkManager.Instance.NetworkProvider.LobbyListReceived += OnLobbyListReceived;
    }

    public override void _ExitTree()
    {
        var provider = NetworkManager.Instance?.NetworkProvider;
        if (provider is null)
            return;

        provider.LobbyCreated -= OnLobbyCreated;
        provider.LobbySessionJoined -= OnLobbySessionJoined;
        provider.ConnectionFailed -= OnError;
        provider.ServerDisconnected -= OnServerDisconnected;
        provider.LobbyListReceived -= OnLobbyListReceived;
    }

    private void OnNicknameChanged(string newText)
    {
        Global.Instance.LocalNickname = newText;
    }

    private void OnShufflePressed()
    {
        _nicknameEdit.Text = RandomNicknames[GD.Randi() % RandomNicknames.Length];
        Global.Instance.LocalNickname = _nicknameEdit.Text;
    }

    private void OnHostPressed()
    {
        _hostButton.Disabled = true;
        NetworkManager.Instance.CreateHostSession();
    }

    private void OnBrowsePressed()
    {
        _homeScreen.Visible = false;
        _browseScreen.Visible = true;
        NetworkManager.Instance.RefreshLobbyList();
    }

    private void ShowHomeScreen()
    {
        _browseScreen.Visible = false;
        _homeScreen.Visible = true;
    }

    private void OnRefreshPressed()
    {
        NetworkManager.Instance.RefreshLobbyList();
    }

    private void JoinSessionEnet()
    {
        var hostAddress = _ipEdit.Text.Trim();
        _joinButton.Disabled = true;
        NetworkManager.Instance.JoinSession(hostAddress: hostAddress);
    }

    private void OnLobbySessionJoined(ulong lobbyId, int guestPeerId, int hostId)
    {
        ResetPendingControls();
        Visible = false;
    }

    private void OnLobbyCreated(ulong lobbyId, int hostPeerId)
    {
        ResetPendingControls();
        Visible = false;
    }

    private void OnError(string msg)
    {
        GD.Print("Error: ", msg);
        ResetPendingControls();
    }

    private void OnServerDisconnected()
    {
        ResetPendingControls();
        ShowHomeScreen();
        Visible = true;
    }

    private void OnLobbyListReceived(Array lobbies)
    {
        _lastLobbies = lobbies;
        RebuildLobbyList(_searchEdit.Text);
    }

    private void OnSearchChanged(string query)
    {
        RebuildLobbyList(query);
    }

    private void RebuildLobbyList(string query)
    {
        foreach (var child in _lobbyListContainer.GetChildren())
            child.QueueFree();

        var normalizedQuery = query.Trim().ToLowerInvariant();
        var matches = 0;

        foreach (var lobbyVariant in _lastLobbies)
        {
            ulong lobbyId = 0;
            var lobbyName = "Unknown Lobby";

            if (lobbyVariant.VariantType == Variant.Type.Int)
            {
                lobbyId = lobbyVariant.AsUInt64();
                lobbyName = "Room " + lobbyId;
            }
            else if (lobbyVariant.VariantType == Variant.Type.Dictionary)
            {
                var lobbyDict = lobbyVariant.AsGodotDictionary();
                lobbyId = lobbyDict.TryGetValue("id", out var idValue) ? idValue.AsUInt64() : 0;
                lobbyName = lobbyDict.TryGetValue("name", out var nameValue) ? nameValue.AsString() : "Room";
            }

            if (normalizedQuery.Length > 0 && !lobbyName.ToLowerInvariant().Contains(normalizedQuery))
                continue;

            matches++;

            var item = (Button)LobbyListItemScene.Instantiate();
            item.Text = $"{lobbyName}  (ID: {lobbyId})";

            var capturedLobbyId = lobbyId;
            item.Pressed += () => OnLobbyItemPressed(capturedLobbyId);
            _lobbyListContainer.AddChild(item);
        }

        _noMatchLabel.Visible = matches == 0;
    }

    private void OnLobbyItemPressed(ulong lobbyId)
    {
        GD.Print("Trying enter in lobby: ", lobbyId);
        _refreshButton.Disabled = true;
        _backButton.Disabled = true;
        NetworkManager.Instance.JoinSession(lobbyId);
    }

    private void ResetPendingControls()
    {
        _hostButton.Disabled = false;
        _joinButton.Disabled = false;
        _refreshButton.Disabled = false;
        _backButton.Disabled = false;
    }
}
