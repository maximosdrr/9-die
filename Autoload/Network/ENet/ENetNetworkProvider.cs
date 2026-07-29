using Godot;

[GlobalClass]
public partial class ENetNetworkProvider : NetworkProvider
{
    private ENetMultiplayerPeer _enet;

    public ENetNetworkProvider()
    {
        _enet = new ENetMultiplayerPeer();
    }

    public override void _Ready()
    {
        base._Ready();
    }

    public override void CreateHost(int port = -1)
    {
        if (port == -1)
        {
            var msg = "Invalid port";
            GD.PushError(msg);
            EmitSignal(SignalName.ConnectionFailed, msg);
        }

        _enet.CreateServer(port);
        Multiplayer.MultiplayerPeer = _enet;

        var peerId = Multiplayer.GetUniqueId();
        var startMessage = $"Host Created. Peer ID: {peerId}";

        GD.Print(startMessage);

        EmitSignal(SignalName.LobbyCreated, 0, peerId);
        EmitSignal(SignalName.PlayerConnected, peerId);
    }

    public override void JoinSession(int lobbyId = 0, string hostAddress = "", int port = -1)
    {
        if (hostAddress == "" || port == -1)
        {
            var msg = "Port or Client address invalid";
            GD.PushError(msg);
            EmitSignal(SignalName.ConnectionFailed, msg);
        }

        _enet.CreateClient(hostAddress, port);
        Multiplayer.MultiplayerPeer = _enet;

        var peerId = Multiplayer.GetUniqueId();
        EmitSignal(SignalName.LobbySessionJoined, 0, peerId, 1);

        var startMessage = $"Joinned Session. Peer ID: {peerId}";
        GD.Print(startMessage);
    }
}
