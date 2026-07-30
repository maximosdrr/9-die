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
        var transport = (string)ProjectSettings.GetSetting("network/transport", "enet");
        NetworkProvider = transport == "steam" ? new SteamNetworkProvider() : new ENetNetworkProvider();
        AddChild(NetworkProvider);
    }

    public void CreateHostSession()
    {
        NetworkProvider.CreateHost(7777);
    }

    public void JoinSession(ulong lobbyId = 0, string hostAddress = "127.0.0.1")
    {
        NetworkProvider.JoinSession(lobbyId, hostAddress, 7777);
    }

    public void RefreshLobbyList()
    {
        NetworkProvider.RefreshLobbyList();
    }
}
