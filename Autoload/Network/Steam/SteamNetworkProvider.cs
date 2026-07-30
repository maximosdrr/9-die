using Godot;

[GlobalClass]
public partial class SteamNetworkProvider : NetworkProvider
{
    public override bool SupportsSessionBrowsing => true;

    public override void _Ready()
    {
        base._Ready();

        var steam = Engine.GetSingleton("Steam");
        steam.Connect("lobby_match_list", new Callable(this, MethodName.OnLobbyMatchList));
        steam.Connect("lobby_created", new Callable(this, MethodName.OnLobbyCreated));
        steam.Connect("lobby_joined", new Callable(this, MethodName.OnLobbyJoined));
    }

    public override void CreateHost(int port = -1)
    {
        var steam = Engine.GetSingleton("Steam");
        var lobbyTypePublic = steam.Get("LOBBY_TYPE_PUBLIC");
        steam.Call("createLobby", lobbyTypePublic, MaxPlayers);
    }

    public override void JoinSession(ulong lobbyId, string hostAddress = "", int port = -1)
    {
        var steam = Engine.GetSingleton("Steam");
        steam.Call("joinLobby", lobbyId);
    }

    public override void RefreshLobbyList()
    {
        var steam = Engine.GetSingleton("Steam");
        var distanceFilterWorldwide = steam.Get("LOBBY_DISTANCE_FILTER_WORLDWIDE");
        var comparisonEqual = steam.Get("LOBBY_COMPARISON_EQUAL");

        steam.Call("addRequestLobbyListDistanceFilter", distanceFilterWorldwide);
        steam.Call("addRequestLobbyListStringFilter", GameIdKey, GameIdValue, comparisonEqual);
        steam.Call("requestLobbyList");
    }

    private void OnLobbyMatchList(Godot.Collections.Array lobbies)
    {
        EmitSignal(SignalName.LobbyListReceived, lobbies);
    }

    private void OnLobbyCreated(long connectResult, ulong lobbyId)
    {
        var steam = Engine.GetSingleton("Steam");
        var resultOk = (long)steam.Get("RESULT_OK");

        if (connectResult != resultOk)
        {
            var msg = "Steam connection error: " + connectResult;
            GD.PushError(msg);
            EmitSignal(SignalName.ConnectionFailed, msg);
            return;
        }

        var personaName = steam.Call("getPersonaName");
        steam.Call("setLobbyData", lobbyId, "name", $"{personaName}'s Game");
        steam.Call("setLobbyData", lobbyId, GameIdKey, GameIdValue);

        Peer = ClassDB.Instantiate("SteamMultiplayerPeer").As<MultiplayerPeer>();
        Peer.Call("create_host", 0);
        Multiplayer.MultiplayerPeer = Peer;

        var peerId = Multiplayer.GetUniqueId();

        EmitSignal(SignalName.LobbyCreated, lobbyId, peerId);
        EmitSignal(SignalName.PlayerConnected, peerId);

        var startMessage = $"Host Session Started. Peer ID: {peerId} Lobby ID: {lobbyId}";
        GD.Print(startMessage);
    }

    private void OnLobbyJoined(ulong lobbyId, long permissions, bool locked, long response)
    {
        var steam = Engine.GetSingleton("Steam");
        var chatRoomEnterResponseSuccess = (long)steam.Get("CHAT_ROOM_ENTER_RESPONSE_SUCCESS");

        if (response != chatRoomEnterResponseSuccess)
        {
            var msg = "Failed to join lobby. Code: " + response;
            EmitSignal(SignalName.ConnectionFailed, msg);
            return;
        }

        var hostId = (ulong)steam.Call("getLobbyOwner", lobbyId);

        if (hostId == (ulong)steam.Call("getSteamID"))
        {
            GD.Print("cannot enter in this room as a guest if you are already the host");
            return;
        }

        Peer = ClassDB.Instantiate("SteamMultiplayerPeer").As<MultiplayerPeer>();
        var createClientResult = (Error)(int)Peer.Call("create_client", hostId, 0);

        if (createClientResult == Error.Ok)
        {
            Multiplayer.MultiplayerPeer = Peer;
            var peerId = Multiplayer.GetUniqueId();

            EmitSignal(SignalName.LobbySessionJoined, lobbyId, peerId, (int)hostId);
            EmitSignal(SignalName.PlayerConnected, peerId);

            var startMessage = $"Client Session Started. Connected to Host: {hostId} . Peer ID: {peerId} ";
            GD.Print(startMessage);
        }
        else
        {
            var msg = "Peer creation failed: " + createClientResult;
            GD.PushError(msg);
            EmitSignal(SignalName.ConnectionFailed, msg);
        }
    }
}
