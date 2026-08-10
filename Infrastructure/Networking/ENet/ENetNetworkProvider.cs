using Godot;

[GlobalClass]
public partial class ENetNetworkProvider : NetworkProvider
{
    public override void CreateHost(int port = -1)
    {
        if (!BeginHostingAttempt())
            return;

        if (!IsValidPort(port))
        {
            FailSession($"Invalid server port: {port}.");
            return;
        }

        var enet = new ENetMultiplayerPeer();
        var result = enet.CreateServer(port, MaxPlayers, GetMaxChannels());
        if (result != Error.Ok)
        {
            enet.Close();
            FailSession($"Could not create ENet server on port {port}: {result}.");
            return;
        }

        AttachPeer(enet);

        var peerId = Multiplayer.GetUniqueId();
        CompleteHosting(0, peerId);
        GD.Print($"ENet host created on port {port}. Peer ID: {peerId}");
    }

    public override void JoinSession(ulong lobbyId = 0, string hostAddress = "", int port = -1)
    {
        if (!BeginConnectionAttempt(lobbyId))
            return;

        if (string.IsNullOrWhiteSpace(hostAddress) || !IsValidPort(port))
        {
            FailSession($"Invalid server address or port: '{hostAddress}:{port}'.");
            return;
        }

        var enet = new ENetMultiplayerPeer();
        var result = enet.CreateClient(hostAddress.Trim(), port, GetMaxChannels());
        if (result != Error.Ok)
        {
            enet.Close();
            FailSession($"Could not start ENet client for {hostAddress}:{port}: {result}.");
            return;
        }

        AttachPeer(enet);
        GD.Print($"Connecting to ENet server {hostAddress}:{port}...");
    }

    private static int GetMaxChannels()
    {
        return (int)ProjectSettings.GetSetting("network/max_channels", 0);
    }
}
