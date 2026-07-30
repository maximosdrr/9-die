using Godot;
using Godot.Collections;

[GlobalClass]
public partial class LobbyMenu : Control
{
    private static readonly PackedScene LobbyListItemScene = GD.Load<PackedScene>("res://Features/Ui/Menus/LobbyListItem.tscn");

    private static readonly string[] RandomNicknames =
    {
        "Taco-Certeiro", "Bola-9-Voadora", "Rei-do-Bar", "Sinuca-Fantasma",
        "Tabela-Dupla", "Mão-de-Ferro", "Giz-Azul", "Caçapa-Facil",
        "Faixa-Verde", "Bicho-do-Feltro", "Tacada-Final", "Mestre-Cueiro",
    };

    private Control _homeScreen;
    private Control _browseScreen;

    private LineEdit _nicknameEdit;
    private Button _shuffleButton;
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
        _homeScreen = GetNode<Control>("HomeScreen");
        _browseScreen = GetNode<Control>("BrowseScreen");

        _nicknameEdit = GetNode<LineEdit>("HomeScreen/CenterContainer/ContentBox/MenuCard/CardMargin/CardBox/IdentityRow/NicknameField/NicknameEdit");
        _shuffleButton = GetNode<Button>("HomeScreen/CenterContainer/ContentBox/MenuCard/CardMargin/CardBox/IdentityRow/ShuffleButton");
        _hostButton = GetNode<Button>("HomeScreen/CenterContainer/ContentBox/MenuCard/CardMargin/CardBox/HostButton");
        _joinButton = GetNode<Button>("HomeScreen/CenterContainer/ContentBox/MenuCard/CardMargin/CardBox/JoinButton");
        _hintLabel = GetNode<Label>("HomeScreen/CenterContainer/ContentBox/MenuCard/CardMargin/CardBox/HintLabel");

        _backButton = GetNode<Button>("BrowseScreen/BrowseMargin/BrowseBox/HeaderRow/BackButton");
        _refreshButton = GetNode<Button>("BrowseScreen/BrowseMargin/BrowseBox/HeaderRow/RefreshButton");
        _searchEdit = GetNode<LineEdit>("BrowseScreen/BrowseMargin/BrowseBox/SearchEdit");
        _lobbyListContainer = GetNode<Control>("BrowseScreen/BrowseMargin/BrowseBox/ScrollContainer/LobbyList");
        _noMatchLabel = GetNode<Label>("BrowseScreen/BrowseMargin/BrowseBox/NoMatchLabel");

        _nicknameEdit.Text = string.IsNullOrWhiteSpace(Global.Instance.LocalNickname)
            ? RandomNicknames[GD.Randi() % RandomNicknames.Length]
            : Global.Instance.LocalNickname;
        Global.Instance.LocalNickname = _nicknameEdit.Text;
        _nicknameEdit.TextChanged += OnNicknameChanged;
        _shuffleButton.Pressed += OnShufflePressed;

        var supportsBrowsing = NetworkManager.Instance.NetworkProvider.SupportsSessionBrowsing;
        _joinButton.Text = supportsBrowsing ? "Buscar Sala" : "Entrar (Local)";
        _hintLabel.Text = supportsBrowsing
            ? "Sem conexão direta disponível — busque uma sala aberta."
            : "Conectando direto em 127.0.0.1 — modo de teste na mesma máquina.";
        _joinButton.Pressed += supportsBrowsing ? OnBrowsePressed : JoinSessionLocal;

        _hostButton.Pressed += OnHostPressed;
        _backButton.Pressed += ShowHomeScreen;
        _refreshButton.Pressed += OnRefreshPressed;
        _searchEdit.TextChanged += OnSearchChanged;

        NetworkManager.Instance.NetworkProvider.LobbyCreated += OnLobbyCreated;
        NetworkManager.Instance.NetworkProvider.LobbySessionJoined += OnLobbySessionJoined;
        NetworkManager.Instance.NetworkProvider.ConnectionFailed += OnError;
        NetworkManager.Instance.NetworkProvider.LobbyListReceived += OnLobbyListReceived;
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
        NetworkManager.Instance.CreateHostSession();
        _hostButton.Disabled = true;
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

    private void JoinSessionLocal()
    {
        NetworkManager.Instance.JoinSession();
        _joinButton.Disabled = true;
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
        _joinButton.Disabled = false;
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

    private void OnLobbyItemPressed(int lobbyId)
    {
        GD.Print("Trying enter in lobby: ", lobbyId);
        NetworkManager.Instance.JoinSession(lobbyId);
        Visible = false;
    }
}
