using Godot;

[GlobalClass]
public partial class NetworkProvider : Node
{
    public const int MaxPlayers = 4;
    public const string GameIdKey = "game";
    public const string GameIdValue = "MyGodotGame";

    [Signal]
    public delegate void LobbyCreatedEventHandler(int lobbyId, int hostPeerId);

    [Signal]
    public delegate void LobbySessionJoinedEventHandler(int lobbyId, int guestPeerId, int hostPeerId);

    [Signal]
    public delegate void LobbyListReceivedEventHandler(Godot.Collections.Array lobbies);

    [Signal]
    public delegate void ConnectionFailedEventHandler(string error);

    [Signal]
    public delegate void PlayerConnectedEventHandler(int id);

    [Signal]
    public delegate void PlayerDisconnectedEventHandler(int id);

    protected MultiplayerPeer Peer;

    public virtual bool SupportsSessionBrowsing => false;

    public override void _Ready()
    {
        Multiplayer.PeerConnected += id => EmitSignal(SignalName.PlayerConnected, (int)id);
        Multiplayer.PeerDisconnected += id => EmitSignal(SignalName.PlayerDisconnected, (int)id);
    }

    public virtual void CreateHost(int port = 7777) { }

    public virtual void JoinSession(int lobbyId, string hostAddress = "", int port = -1) { }

    public virtual void RefreshLobbyList() { }
}
