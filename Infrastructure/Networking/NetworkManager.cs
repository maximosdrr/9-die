using Godot;

public partial class NetworkManager : Node
{
    private const int FallbackPort = 7777;
    private const string FallbackHostAddress = "127.0.0.1";

    public static NetworkManager Instance { get; private set; }

    public NetworkProvider NetworkProvider;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        var transport = ProjectSettings.GetSetting("network/transport", "enet").AsString();
        NetworkProvider = transport.Equals("steam", System.StringComparison.OrdinalIgnoreCase)
            ? new SteamNetworkProvider()
            : new ENetNetworkProvider();
        AddChild(NetworkProvider);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public void CreateHostSession()
    {
        NetworkProvider.CreateHost(GetConfiguredPort());
    }

    public void JoinSession(ulong lobbyId = 0, string hostAddress = "")
    {
        var resolvedAddress = string.IsNullOrWhiteSpace(hostAddress)
            ? GetConfiguredHostAddress()
            : hostAddress.Trim();
        NetworkProvider.JoinSession(lobbyId, resolvedAddress, GetConfiguredPort());
    }

    public void RefreshLobbyList()
    {
        NetworkProvider.RefreshLobbyList();
    }

    internal static int GetConfiguredPort()
    {
        return (int)ProjectSettings.GetSetting("network/port", FallbackPort);
    }

    internal static string GetConfiguredHostAddress()
    {
        var configured = ProjectSettings.GetSetting("network/default_host_address", FallbackHostAddress)
            .AsString()
            .Trim();
        return string.IsNullOrEmpty(configured) ? FallbackHostAddress : configured;
    }
}
