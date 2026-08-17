using System.Collections.Generic;
using Godot;

[GlobalClass]
public partial class SteamNetworkProvider : NetworkProvider
{
    internal const string HostWithLobbyMethod = "host_with_lobby";
    internal const string ConnectToLobbyMethod = "connect_to_lobby";

    private GodotObject _steam;
    private ulong _activeLobbyId;
    private readonly HashSet<ulong> _publicLobbyIds = new();
    private readonly HashSet<ulong> _friendLobbyIds = new();
    private bool _lobbyListRequestInFlight;
    private bool _lobbyListRefreshQueued;

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
        _steam.Connect("lobby_data_update", new Callable(this, MethodName.OnLobbyDataUpdate));
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
        DisconnectSteamSignal("lobby_data_update", MethodName.OnLobbyDataUpdate);
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

        if (_lobbyListRequestInFlight)
        {
            _lobbyListRefreshQueued = true;
            return;
        }

        QueryLobbyList();
    }

    private void QueryLobbyList()
    {
        _lobbyListRequestInFlight = true;
        _lobbyListRefreshQueued = false;

        var distanceFilterWorldwide = _steam.Get("LOBBY_DISTANCE_FILTER_WORLDWIDE");
        var comparisonEqual = _steam.Get("LOBBY_COMPARISON_EQUAL");

        CollectFriendLobbies();
        _publicLobbyIds.Clear();

        _steam.Call("addRequestLobbyListDistanceFilter", distanceFilterWorldwide);
        _steam.Call("addRequestLobbyListStringFilter", GameIdKey, GameId, comparisonEqual);

        GD.Print($"Requesting Steam lobbies: game={GameId}; friend lobbies={_friendLobbyIds.Count}");
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
        _lobbyListRequestInFlight = false;
        _publicLobbyIds.Clear();
        foreach (var lobby in lobbies)
        {
            var lobbyId = ExtractLobbyId(lobby);
            if (lobbyId != 0)
                _publicLobbyIds.Add(lobbyId);
        }

        EmitVisibleLobbyList();

        if (_lobbyListRefreshQueued && IsInsideTree() && _steam is not null)
            QueryLobbyList();
    }

    // GodotSteam emits success first, followed by the lobby and member IDs.
    private void OnLobbyDataUpdate(long success, ulong lobbyId, ulong memberId)
    {
        if (success != 1 || !_friendLobbyIds.Contains(lobbyId))
            return;

        // Friend lobby metadata can arrive after the public query. Rebuild the real Steam result
        // when it does instead of fabricating a local list entry.
        EmitVisibleLobbyList();
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
        var publishedName = _steam.Call(
            "setLobbyData",
            lobbyId,
            "name",
            $"{personaName}'s Game").AsBool();
        var publishedGame = _steam.Call("setLobbyData", lobbyId, GameIdKey, GameId).AsBool();
        var publishedProtocol = _steam.Call(
            "setLobbyData",
            lobbyId,
            ProtocolVersionKey,
            ProtocolVersion).AsBool();

        GD.Print(
            $"Steam lobby metadata: lobby={lobbyId}, name={publishedName}, "
            + $"game={publishedGame}, protocol={publishedProtocol}");
        if (!publishedGame || !publishedProtocol)
        {
            LeaveActiveLobby();
            FailSession("Steam lobby was created, but its discovery metadata could not be published.");
            return;
        }
        if (!publishedName)
            GD.PushWarning($"Steam lobby {lobbyId} is public, but its custom name was not published.");

        var peer = ClassDB.Instantiate("SteamMultiplayerPeer").As<MultiplayerPeer>();
        var createHostResult = (Error)peer.Call(HostWithLobbyMethod, lobbyId).AsInt32();
        if (createHostResult != Error.Ok)
        {
            peer.Close();
            LeaveActiveLobby();
            FailSession("Steam peer creation failed: " + createHostResult);
            return;
        }

        AttachPeer(peer);

        var peerId = Multiplayer.GetUniqueId();
        GD.Print($"Steam gameplay peer bound to lobby {lobbyId} as host.");

        CompleteHosting(lobbyId, peerId);

        var startMessage = $"Host Session Started. Peer ID: {peerId} Lobby ID: {lobbyId}";
        GD.Print(startMessage);
    }

    private void OnLobbyJoined(ulong lobbyId, long permissions, bool locked, long response)
    {
        if (_steam is null)
            return;

        // host_with_lobby() makes the host join their own lobby as a side effect, which
        // echoes this same "lobby_joined" signal back to the host. That is expected and
        // not a stray guest join attempt, so it must not trigger leaveLobby on ourselves
        // (which would silently pull the host out of the lobby they just created).
        if (SessionState == NetworkSessionState.Hosting && lobbyId == _activeLobbyId)
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
        var createClientResult = (Error)peer.Call(ConnectToLobbyMethod, lobbyId).AsInt32();

        if (createClientResult == Error.Ok)
        {
            AttachPeer(peer);
            GD.Print($"Connecting Steam client through lobby {lobbyId} to host {hostId}...");
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

    private void CollectFriendLobbies()
    {
        _friendLobbyIds.Clear();
        if (_steam is null)
            return;

        var immediateFriends = _steam.Get("FRIEND_FLAG_IMMEDIATE");
        var friendCount = _steam.Call("getFriendCount", immediateFriends).AsInt32();
        for (var index = 0; index < friendCount; index++)
        {
            var friendId = _steam.Call("getFriendByIndex", index, immediateFriends).AsUInt64();
            var gameInfoVariant = _steam.Call("getFriendGamePlayed", friendId);
            if (gameInfoVariant.VariantType != Variant.Type.Dictionary)
                continue;

            var lobbyId = ExtractFriendLobbyId(gameInfoVariant.AsGodotDictionary());
            if (lobbyId == 0 || !_friendLobbyIds.Add(lobbyId))
                continue;

            _steam.Call("requestLobbyData", lobbyId);
        }
    }

    private void EmitVisibleLobbyList()
    {
        var visibleLobbies = new Godot.Collections.Array();
        var emittedLobbyIds = new HashSet<ulong>();

        AddVisibleLobbyEntries(_publicLobbyIds, emittedLobbyIds, visibleLobbies);
        AddVisibleLobbyEntries(_friendLobbyIds, emittedLobbyIds, visibleLobbies);

        GD.Print(
            $"Steam lobby search: public={_publicLobbyIds.Count}, "
            + $"friends={_friendLobbyIds.Count}, compatible={visibleLobbies.Count}.");
        EmitSignal(SignalName.LobbyListReceived, visibleLobbies);
    }

    private void AddVisibleLobbyEntries(
        IEnumerable<ulong> source,
        HashSet<ulong> emittedLobbyIds,
        Godot.Collections.Array destination)
    {
        foreach (var lobbyId in source)
        {
            if (!emittedLobbyIds.Add(lobbyId) || !TryCreateLobbyEntry(lobbyId, out var entry))
                continue;

            destination.Add(entry);
        }
    }

    private bool TryCreateLobbyEntry(
        ulong lobbyId,
        out Godot.Collections.Dictionary entry)
    {
        entry = null;
        if (_steam is null || lobbyId == 0)
            return false;

        var game = _steam.Call("getLobbyData", lobbyId, GameIdKey).AsString();
        var protocol = _steam.Call("getLobbyData", lobbyId, ProtocolVersionKey).AsString();
        if (!IsCompatibleLobbyMetadata(game, protocol))
            return false;

        var name = _steam.Call("getLobbyData", lobbyId, "name").AsString().Trim();
        if (string.IsNullOrEmpty(name))
            name = $"Room {lobbyId}";

        entry = new Godot.Collections.Dictionary
        {
            ["id"] = lobbyId,
            ["name"] = name,
        };
        return true;
    }

    internal static bool IsCompatibleLobbyMetadata(string game, string protocol)
    {
        var normalizedGame = game?.Trim() ?? string.Empty;
        var normalizedProtocol = protocol?.Trim() ?? string.Empty;
        return normalizedGame == GameId
            && (normalizedProtocol.Length == 0 || normalizedProtocol == ProtocolVersion);
    }

    internal static ulong ExtractFriendLobbyId(Godot.Collections.Dictionary gameInfo)
    {
        if (gameInfo is null || !gameInfo.TryGetValue("lobby", out var lobby))
            return 0;
        return lobby.AsUInt64();
    }

    private static ulong ExtractLobbyId(Variant lobby)
    {
        if (lobby.VariantType == Variant.Type.Int)
            return lobby.AsUInt64();
        if (lobby.VariantType != Variant.Type.Dictionary)
            return 0;

        var dictionary = lobby.AsGodotDictionary();
        return dictionary.TryGetValue("id", out var id) ? id.AsUInt64() : 0;
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
