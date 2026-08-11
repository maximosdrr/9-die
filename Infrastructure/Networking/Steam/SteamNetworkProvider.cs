using Godot;

[GlobalClass]
public partial class SteamNetworkProvider : NetworkProvider
{
    private GodotObject _steam;
    private ulong _activeLobbyId;

    public override bool SupportsSessionBrowsing => true;

    public override void _Ready()
    {
        base._Ready();

        if (!Engine.HasSingleton("Steam"))
        {
            FailSession("Steam singleton is unavailable.");
            return;
        }

        _steam = Engine.GetSingleton("Steam");
        _steam.Connect("lobby_match_list", new Callable(this, MethodName.OnLobbyMatchList));
        _steam.Connect("lobby_created", new Callable(this, MethodName.OnLobbyCreated));
        _steam.Connect("lobby_joined", new Callable(this, MethodName.OnLobbyJoined));
        ConnectionFailed += OnSessionEnded;
        ServerDisconnected += OnSessionEnded;
    }

    public override void _ExitTree()
    {
        ConnectionFailed -= OnSessionEnded;
        ServerDisconnected -= OnSessionEnded;
        LeaveActiveLobby();
        DisconnectSteamSignal("lobby_match_list", MethodName.OnLobbyMatchList);
        DisconnectSteamSignal("lobby_created", MethodName.OnLobbyCreated);
        DisconnectSteamSignal("lobby_joined", MethodName.OnLobbyJoined);
        _steam = null;
        base._ExitTree();
    }

    public override void CreateHost(int port = -1)
    {
        if (!BeginHostingAttempt())
            return;

        if (_steam is null)
        {
            FailSession("Steam is not initialized.");
            return;
        }

        var lobbyTypePublic = _steam.Get("LOBBY_TYPE_PUBLIC");
        _steam.Call("createLobby", lobbyTypePublic, MaxPlayers);
    }

    public override void JoinSession(ulong lobbyId, string hostAddress = "", int port = -1)
    {
        if (!BeginConnectionAttempt(lobbyId))
            return;

        if (_steam is null || lobbyId == 0)
        {
            FailSession(lobbyId == 0 ? "Invalid Steam lobby ID." : "Steam is not initialized.");
            return;
        }

        _steam.Call("joinLobby", lobbyId);
    }

    public override void RefreshLobbyList()
    {
        if (_steam is null)
        {
            EmitSignal(SignalName.ConnectionFailed, "Steam is not initialized.");
            return;
        }

        var distanceFilterWorldwide = _steam.Get("LOBBY_DISTANCE_FILTER_WORLDWIDE");
        var comparisonEqual = _steam.Get("LOBBY_COMPARISON_EQUAL");

        _steam.Call("addRequestLobbyListDistanceFilter", distanceFilterWorldwide);
        _steam.Call("addRequestLobbyListStringFilter", GameIdKey, GameId, comparisonEqual);
        _steam.Call("addRequestLobbyListStringFilter", ProtocolVersionKey, ProtocolVersion, comparisonEqual);
        _steam.Call("requestLobbyList");
    }

    // Lets callers (e.g. TvScreenShare) address peers directly over Steam's raw P2P networking
    // instead of Godot's high-level MultiplayerApi, which SteamMultiplayerPeer funnels through a
    // single underlying connection regardless of the RPC transfer channel used.
    public ulong GetSteamId(int peerId) =>
        Peer != null && GodotObject.IsInstanceValid(Peer)
            ? Peer.Call("get_steam_id_for_peer_id", peerId).AsUInt64()
            : 0;

    public int GetPeerId(ulong steamId) =>
        Peer != null && GodotObject.IsInstanceValid(Peer)
            ? Peer.Call("get_peer_id_for_steam_id", steamId).AsInt32()
            : 0;

    private void OnLobbyMatchList(Godot.Collections.Array lobbies)
    {
        EmitSignal(SignalName.LobbyListReceived, lobbies);
    }

    private void OnLobbyCreated(long connectResult, ulong lobbyId)
    {
        if (_steam is null)
            return;

        if (SessionState != NetworkSessionState.StartingHost)
        {
            if (lobbyId != 0)
                _steam.Call("leaveLobby", lobbyId);
            return;
        }

        var resultOk = (long)_steam.Get("RESULT_OK");

        if (connectResult != resultOk)
        {
            FailSession("Steam connection error: " + connectResult);
            return;
        }

        var personaName = _steam.Call("getPersonaName");
        _activeLobbyId = lobbyId;
        _steam.Call("setLobbyData", lobbyId, "name", $"{personaName}'s Game");
        _steam.Call("setLobbyData", lobbyId, GameIdKey, GameId);
        _steam.Call("setLobbyData", lobbyId, ProtocolVersionKey, ProtocolVersion);

        var peer = ClassDB.Instantiate("SteamMultiplayerPeer").As<MultiplayerPeer>();
        var createHostResult = (Error)peer.Call("create_host", 0).AsInt32();
        if (createHostResult != Error.Ok)
        {
            peer.Close();
            LeaveActiveLobby();
            FailSession("Steam peer creation failed: " + createHostResult);
            return;
        }

        AttachPeer(peer);

        var peerId = Multiplayer.GetUniqueId();

        CompleteHosting(lobbyId, peerId);

        var startMessage = $"Host Session Started. Peer ID: {peerId} Lobby ID: {lobbyId}";
        GD.Print(startMessage);
    }

    private void OnLobbyJoined(ulong lobbyId, long permissions, bool locked, long response)
    {
        if (_steam is null)
            return;

        if (SessionState != NetworkSessionState.Connecting)
        {
            if (lobbyId != 0)
                _steam.Call("leaveLobby", lobbyId);
            return;
        }

        var chatRoomEnterResponseSuccess = (long)_steam.Get("CHAT_ROOM_ENTER_RESPONSE_SUCCESS");

        if (response != chatRoomEnterResponseSuccess)
        {
            FailSession("Failed to join lobby. Code: " + response);
            return;
        }

        _activeLobbyId = lobbyId;
        var hostId = (ulong)_steam.Call("getLobbyOwner", lobbyId);

        if (hostId == (ulong)_steam.Call("getSteamID"))
        {
            LeaveActiveLobby();
            FailSession("Cannot join your own Steam lobby as a guest.");
            return;
        }

        var peer = ClassDB.Instantiate("SteamMultiplayerPeer").As<MultiplayerPeer>();
        var createClientResult = (Error)peer.Call("create_client", hostId, 0).AsInt32();

        if (createClientResult == Error.Ok)
        {
            AttachPeer(peer);
            GD.Print($"Connecting Steam client to host {hostId}...");
        }
        else
        {
            peer.Close();
            LeaveActiveLobby();
            FailSession("Peer creation failed: " + createClientResult);
        }
    }

    private void OnSessionEnded(string error)
    {
        LeaveActiveLobby();
    }

    private void OnSessionEnded()
    {
        LeaveActiveLobby();
    }

    private void LeaveActiveLobby()
    {
        if (_steam is null || _activeLobbyId == 0)
            return;

        _steam.Call("leaveLobby", _activeLobbyId);
        _activeLobbyId = 0;
    }

    private void DisconnectSteamSignal(StringName signal, StringName method)
    {
        if (_steam is null || !GodotObject.IsInstanceValid(_steam))
            return;

        var callable = new Callable(this, method);
        if (_steam.IsConnected(signal, callable))
            _steam.Disconnect(signal, callable);
    }
}
