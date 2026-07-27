using Godot;

public partial class NetworkManager : Node
{
    public static NetworkManager Instance { get; private set; }

    public NetworkProvider NetworkProvider;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        NetworkProvider = new ENetNetworkProvider();
        // NetworkProvider = new SteamNetworkProvider();
        AddChild(NetworkProvider);
    }

    public void CreateHostSession()
    {
        NetworkProvider.CreateHost(7777);
    }

    public void JoinSession(int lobbyId = 0)
    {
        NetworkProvider.JoinSession(lobbyId, "127.0.0.1", 7777);
    }

    public void RefreshLobbyList()
    {
        NetworkProvider.RefreshLobbyList();
    }
}
