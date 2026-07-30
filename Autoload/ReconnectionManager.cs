using Godot;
using System.Collections.Generic;

public partial class ReconnectionManager : Node
{
    private const double GraceSeconds = 60.0;

    public static ReconnectionManager Instance { get; private set; }

    private readonly Dictionary<int, string> _peerTokens = new();
    private readonly Dictionary<string, PendingReconnection> _pending = new();

    private readonly struct PendingReconnection
    {
        public readonly string OldPlayerId;
        public readonly TableGame Table;

        public PendingReconnection(string oldPlayerId, TableGame table)
        {
            OldPlayerId = oldPlayerId;
            Table = table;
        }
    }

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        NetworkManager.Instance.NetworkProvider.PlayerConnected += OnPlayerConnected;
    }

    // Fires once per peer, including a client's own first connection (id 1 = the
    // server). That's the client's cue to hand the server its persistent identity.
    private void OnPlayerConnected(int peerId)
    {
        if (Multiplayer.IsServer() || peerId != 1)
            return;

        RpcId(1, MethodName.RegisterToken, Global.Instance.ReconnectToken);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RegisterToken(string token)
    {
        if (!Multiplayer.IsServer() || string.IsNullOrEmpty(token))
            return;

        var peerId = Multiplayer.GetRemoteSenderId();
        _peerTokens[peerId] = token;

        if (!_pending.TryGetValue(token, out var pending))
            return;

        _pending.Remove(token);

        if (IsInstanceValid(pending.Table))
            pending.Table.ReclaimSlot(pending.OldPlayerId, peerId.ToString());
    }

    public string GetToken(int peerId)
    {
        return _peerTokens.TryGetValue(peerId, out var token) ? token : null;
    }

    // Called by TableGame when a player who's part of an active match disconnects.
    // Holds their slot for GraceSeconds instead of forfeiting right away, giving a
    // dropped connection a real chance to reconnect and pick up where they left off.
    public void BeginGracePeriod(string token, string oldPlayerId, TableGame table)
    {
        if (!Multiplayer.IsServer() || string.IsNullOrEmpty(token))
            return;

        _pending[token] = new PendingReconnection(oldPlayerId, table);
        GetTree().CreateTimer(GraceSeconds).Timeout += () => ExpireGracePeriod(token);
    }

    private void ExpireGracePeriod(string token)
    {
        // Already reclaimed by RegisterToken before the timer fired — nothing to do.
        if (!_pending.TryGetValue(token, out var pending))
            return;

        _pending.Remove(token);

        if (IsInstanceValid(pending.Table))
            pending.Table.RemovePlayerFromMatch(pending.OldPlayerId, "opponent_disconnected");
    }
}
