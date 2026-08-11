using System;
using Godot;

public enum NetworkSessionState
{
    Offline,
    StartingHost,
    Connecting,
    Hosting,
    Connected,
    Failed,
}

[GlobalClass]
public partial class NetworkProvider : Node
{
    private const int ServerPeerId = 1;
    private const double FallbackSessionAttemptTimeoutSeconds = 15.0;

    private bool _multiplayerSignalsConnected;
    private ulong _pendingLobbyId;
    private ulong _sessionAttemptRevision;

    public static int MaxPlayers => (int)ProjectSettings.GetSetting("network/max_players", 4);
    public static string GameId => ReadNonEmptySetting("network/game_id", "9die");
    public static string ProtocolVersion => ReadNonEmptySetting("network/protocol_version", "2");
    public static double SessionAttemptTimeoutSeconds => Math.Clamp(
        ProjectSettings.GetSetting(
            "network/session_attempt_timeout_seconds",
            FallbackSessionAttemptTimeoutSeconds).AsDouble(),
        0.05,
        120.0);

    public const string GameIdKey = "game";
    public const string ProtocolVersionKey = "protocol_version";

    public NetworkSessionState SessionState { get; private set; } = NetworkSessionState.Offline;

    [Signal]
    public delegate void LobbyCreatedEventHandler(ulong lobbyId, int hostPeerId);

    [Signal]
    public delegate void LobbySessionJoinedEventHandler(ulong lobbyId, int guestPeerId, int hostPeerId);

    [Signal]
    public delegate void LobbyListReceivedEventHandler(Godot.Collections.Array lobbies);

    [Signal]
    public delegate void ConnectionFailedEventHandler(string error);

    [Signal]
    public delegate void ServerDisconnectedEventHandler();

    [Signal]
    public delegate void SessionStateChangedEventHandler(int previousState, int currentState, string detail);

    [Signal]
    public delegate void PlayerConnectedEventHandler(int id);

    [Signal]
    public delegate void PlayerDisconnectedEventHandler(int id);

    protected MultiplayerPeer Peer;

    public virtual bool SupportsSessionBrowsing => false;

    public override void _Ready()
    {
        ConnectMultiplayerSignals();
    }

    public override void _ExitTree()
    {
        InvalidateSessionAttempt();
        DisconnectMultiplayerSignals();
        ReleasePeer();
        SetSessionState(NetworkSessionState.Offline);
    }

    public virtual void CreateHost(int port = -1) { }

    public virtual void JoinSession(ulong lobbyId, string hostAddress = "", int port = -1) { }

    public virtual void RefreshLobbyList() { }

    protected internal bool BeginHostingAttempt()
    {
        if (!CanStartSession())
        {
            ReportRejectedOperation("A network session is already active.");
            return false;
        }

        ReleasePeer();
        _pendingLobbyId = 0;
        SetSessionState(NetworkSessionState.StartingHost);
        StartSessionAttemptTimeout(NetworkSessionState.StartingHost);
        return true;
    }

    protected internal bool BeginConnectionAttempt(ulong lobbyId)
    {
        if (!CanStartSession())
        {
            ReportRejectedOperation("A network session is already active.");
            return false;
        }

        ReleasePeer();
        _pendingLobbyId = lobbyId;
        SetSessionState(NetworkSessionState.Connecting);
        StartSessionAttemptTimeout(NetworkSessionState.Connecting);
        return true;
    }

    protected void AttachPeer(MultiplayerPeer peer)
    {
        Peer = peer;
        Multiplayer.MultiplayerPeer = peer;
    }

    protected internal void CompleteHosting(ulong lobbyId, int hostPeerId)
    {
        if (SessionState != NetworkSessionState.StartingHost)
            return;

        InvalidateSessionAttempt();
        SetSessionState(NetworkSessionState.Hosting);
        EmitSignal(SignalName.LobbyCreated, lobbyId, hostPeerId);

        // Godot only reports remote peers through PeerConnected. The listen-server's local player
        // still needs one deterministic spawn notification.
        EmitSignal(SignalName.PlayerConnected, hostPeerId);
    }

    protected void FailSession(string error)
    {
        var message = string.IsNullOrWhiteSpace(error) ? "Network session failed." : error;
        InvalidateSessionAttempt();
        ReleasePeer();
        _pendingLobbyId = 0;
        SetSessionState(NetworkSessionState.Failed, message);
        // Connection failures are expected runtime outcomes, not engine faults. The signal drives
        // user-facing recovery while a warning preserves diagnostics without poisoning test logs.
        GD.PushWarning(message);
        EmitSignal(SignalName.ConnectionFailed, message);
    }

    protected static bool IsValidPort(int port)
    {
        return port is >= 1 and <= 65_535;
    }

    private bool CanStartSession()
    {
        return SessionState is NetworkSessionState.Offline or NetworkSessionState.Failed;
    }

    private void ConnectMultiplayerSignals()
    {
        if (_multiplayerSignalsConnected)
            return;

        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        _multiplayerSignalsConnected = true;
    }

    private void DisconnectMultiplayerSignals()
    {
        if (!_multiplayerSignalsConnected)
            return;

        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        _multiplayerSignalsConnected = false;
    }

    private void OnPeerConnected(long peerId)
    {
        EmitSignal(SignalName.PlayerConnected, checked((int)peerId));
    }

    private void OnPeerDisconnected(long peerId)
    {
        EmitSignal(SignalName.PlayerDisconnected, checked((int)peerId));
    }

    private void OnConnectedToServer()
    {
        CompleteConnection(Multiplayer.GetUniqueId());
    }

    protected internal void CompleteConnection(int guestPeerId)
    {
        if (SessionState != NetworkSessionState.Connecting)
            return;

        var lobbyId = _pendingLobbyId;
        _pendingLobbyId = 0;

        InvalidateSessionAttempt();
        SetSessionState(NetworkSessionState.Connected);
        EmitSignal(SignalName.LobbySessionJoined, lobbyId, guestPeerId, ServerPeerId);
    }

    private void OnConnectionFailed()
    {
        if (SessionState != NetworkSessionState.Connecting)
            return;

        FailSession("Could not connect to the server.");
    }

    private void OnServerDisconnected()
    {
        CompleteServerDisconnection();
    }

    protected internal void CompleteServerDisconnection()
    {
        if (SessionState == NetworkSessionState.Connecting)
        {
            FailSession("Server disconnected while the connection was being established.");
            return;
        }

        if (SessionState != NetworkSessionState.Connected)
            return;

        InvalidateSessionAttempt();
        ReleasePeer();
        _pendingLobbyId = 0;
        SetSessionState(NetworkSessionState.Offline, "Server disconnected.");
        EmitSignal(SignalName.ServerDisconnected);
    }

    private void ReleasePeer()
    {
        if (Peer is null)
            return;

        if (GodotObject.IsInstanceValid(Peer))
        {
            var activePeer = Multiplayer.MultiplayerPeer;
            if (activePeer is not null
                && GodotObject.IsInstanceValid(activePeer)
                && activePeer.GetInstanceId() == Peer.GetInstanceId())
            {
                Multiplayer.MultiplayerPeer = null;
            }

            Peer.Close();
        }

        Peer = null;
    }

    private void ReportRejectedOperation(string message)
    {
        GD.PushWarning(message);
        EmitSignal(SignalName.ConnectionFailed, message);
    }

    private void StartSessionAttemptTimeout(NetworkSessionState expectedState)
    {
        var revision = ++_sessionAttemptRevision;
        WatchSessionAttemptTimeout(revision, expectedState);
    }

    private async void WatchSessionAttemptTimeout(
        ulong revision,
        NetworkSessionState expectedState)
    {
        var tree = GetTree();
        if (tree is null)
            return;

        var timer = tree.CreateTimer(SessionAttemptTimeoutSeconds, processAlways: true);
        await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);

        if (!IsInsideTree()
            || revision != _sessionAttemptRevision
            || SessionState != expectedState)
        {
            return;
        }

        FailSession(expectedState == NetworkSessionState.StartingHost
            ? "Timed out while creating the host session."
            : "Timed out while connecting to the server.");
    }

    private void InvalidateSessionAttempt()
    {
        _sessionAttemptRevision++;
    }

    private void SetSessionState(NetworkSessionState state, string detail = "")
    {
        if (SessionState == state)
            return;

        var previous = SessionState;
        SessionState = state;
        EmitSignal(SignalName.SessionStateChanged, (int)previous, (int)state, detail);
    }

    private static string ReadNonEmptySetting(string path, string fallback)
    {
        var value = ProjectSettings.GetSetting(path, fallback).AsString().Trim();
        return string.IsNullOrEmpty(value) ? fallback : value;
    }
}
