using System;
using System.Collections.Generic;
using Godot;

public partial class ReconnectionManager : Node
{
    private const double GraceSeconds = 60.0;
    private const int TokenRequestsPerSecond = 4;
    private const int MaxTrackedTokenRequestPeers = 16;

    public static ReconnectionManager Instance { get; private set; }

    private readonly Dictionary<int, string> _peerTokens = new();
    private readonly Dictionary<string, int> _connectedPeersByToken = new();
    private readonly Dictionary<string, PendingReconnection> _pending = new();
    private readonly HashSet<int> _disconnectingPeers = new();
    private readonly PeerRequestRateLimiter _tokenRequestLimiter = new(
        TokenRequestsPerSecond, 1_000, MaxTrackedTokenRequestPeers);
    private ulong _nextPendingRevision;

    private readonly struct PendingReconnection
    {
        public readonly string OldPlayerId;
        public readonly TableGame Table;
        public readonly ulong Revision;

        public PendingReconnection(string oldPlayerId, TableGame table, ulong revision)
        {
            OldPlayerId = oldPlayerId;
            Table = table;
            Revision = revision;
        }
    }

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        NetworkManager.Instance.NetworkProvider.PlayerConnected += OnPlayerConnected;
        NetworkManager.Instance.NetworkProvider.PlayerDisconnected += OnPlayerDisconnected;
    }

    public override void _ExitTree()
    {
        var provider = NetworkManager.Instance?.NetworkProvider;
        if (provider is not null)
        {
            provider.PlayerConnected -= OnPlayerConnected;
            provider.PlayerDisconnected -= OnPlayerDisconnected;
        }

        _peerTokens.Clear();
        _connectedPeersByToken.Clear();
        _pending.Clear();
        _disconnectingPeers.Clear();
        _tokenRequestLimiter.Clear();

        if (Instance == this)
            Instance = null;
    }

    // A client receives peer 1 when its connection is established. Only then is
    // it safe to submit the persistent identity used during a short reconnect.
    private void OnPlayerConnected(int peerId)
    {
        if (Multiplayer.IsServer() || peerId != 1)
            return;

        RpcId(1, MethodName.RegisterToken, Global.Instance.ReconnectToken);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RegisterToken(string token)
    {
        if (!Multiplayer.IsServer())
            return;

        var peerId = Multiplayer.GetRemoteSenderId();
        if (peerId <= 0 || !_tokenRequestLimiter.TryConsume(peerId)
            || !IsValidReconnectToken(token))
        {
            return;
        }

        // A token is a temporary identity credential. Never let a second live
        // peer claim it. A peer already observed disconnecting is no longer live, though, and a
        // reconnect can arrive before its deferred token cleanup runs.
        if (_connectedPeersByToken.TryGetValue(token, out var ownerPeerId) && ownerPeerId != peerId)
        {
            if (!CanClaimConnectedToken(
                peerId, ownerPeerId, _disconnectingPeers.Contains(ownerPeerId)))
            {
                GD.PushWarning($"Rejected duplicate reconnect token from peer {peerId}.");
                return;
            }

            _peerTokens.Remove(ownerPeerId);
        }

        if (_peerTokens.TryGetValue(peerId, out var previousToken) && previousToken != token)
            _connectedPeersByToken.Remove(previousToken);

        _peerTokens[peerId] = token;
        _connectedPeersByToken[token] = peerId;

        if (!_pending.Remove(token, out var pending))
            return;

        if (IsInstanceValid(pending.Table))
            pending.Table.ReclaimSlot(pending.OldPlayerId, peerId.ToString());
    }

    public string GetToken(int peerId)
    {
        return _peerTokens.TryGetValue(peerId, out var token) ? token : null;
    }

    // Called by TableGame when an active participant disconnects. Their seat is
    // held briefly instead of being forfeited immediately.
    public void BeginGracePeriod(string token, string oldPlayerId, TableGame table)
    {
        if (!Multiplayer.IsServer() || !IsValidReconnectToken(token) || string.IsNullOrEmpty(oldPlayerId) || !IsInstanceValid(table))
            return;

        var revision = ++_nextPendingRevision;
        _pending[token] = new PendingReconnection(oldPlayerId, table, revision);
        GetTree().CreateTimer(GraceSeconds).Timeout += () => ExpireGracePeriod(token, revision);
    }

    private void ExpireGracePeriod(string token, ulong revision)
    {
        // Ignore both reclaimed seats and stale timers from an older disconnect.
        if (!_pending.TryGetValue(token, out var pending) || pending.Revision != revision)
            return;

        _pending.Remove(token);

        if (IsInstanceValid(pending.Table))
            pending.Table.RemovePlayerFromMatch(pending.OldPlayerId, "opponent_disconnected");
    }

    private void OnPlayerDisconnected(int peerId)
    {
        if (!Multiplayer.IsServer())
            return;

        // Mark it immediately so a fast reconnect can take ownership even before deferred
        // cleanup. TableGame consumes the same signal and still needs the token while handling it,
        // so the dictionaries themselves remain until the deferred call.
        _disconnectingPeers.Add(peerId);
        CallDeferred(MethodName.ForgetPeerToken, peerId);
    }

    private void ForgetPeerToken(int peerId)
    {
        _disconnectingPeers.Remove(peerId);

        if (!_peerTokens.Remove(peerId, out var token))
            return;

        if (_connectedPeersByToken.TryGetValue(token, out var ownerPeerId) && ownerPeerId == peerId)
            _connectedPeersByToken.Remove(token);
    }

    internal static bool IsValidReconnectToken(string token)
    {
        return Guid.TryParseExact(token, "N", out var parsed) && parsed != Guid.Empty;
    }

    internal bool TryConsumeTokenRequest(int peerId, ulong nowMilliseconds) =>
        _tokenRequestLimiter.TryConsume(peerId, nowMilliseconds);

    internal static bool CanClaimConnectedToken(
        int claimantPeerId, int ownerPeerId, bool ownerIsDisconnecting)
    {
        return claimantPeerId == ownerPeerId || ownerIsDisconnecting;
    }
}
