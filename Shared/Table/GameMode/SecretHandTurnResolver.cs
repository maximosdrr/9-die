using System.Collections.Generic;
using Godot;
using Godot.Collections;

/// <summary>
/// Runs a hidden-information match server side and is the only place a private holding ever exists.
///
/// The secrecy contract, stated once: <see cref="Hands"/> and <see cref="DealSeed"/> never leave
/// this node. A holding reaches its owner and nobody else, through the single targeted
/// <see cref="ReceiveHand"/>. The seed in particular would reconstruct every holding at once, so it
/// is never put in a context, a broadcast or a log.
///
/// Everything public rides the turn context that TableTurnNetworkBridge already replicates. Reusing
/// that channel rather than adding a second one means the public state can never arrive out of step
/// with the turn it belongs to.
///
/// This node belongs to the server, unlike CueNetworkBridge which belongs to a player, so
/// RpcMode.Authority is correct for every server-to-client message here.
///
/// A game subclasses this to add its own rules, its own request RPCs and its own public context.
/// What it inherits is the part that must not be reinvented per game, because getting any of it
/// wrong leaks cards: the private channel, the turn stamp, and the catch-up for late joiners.
/// </summary>
[GlobalClass]
public partial class SecretHandTurnResolver : TurnResolver
{
    /// <summary>The match. Subclasses read their own type off this.</summary>
    protected TableGame Table;

    /// <summary>
    /// Every player's private holding, as item ids. THE server secret — nothing here is ever put on
    /// a broadcast, and the only route out is <see cref="SendHand"/>.
    /// </summary>
    protected readonly System.Collections.Generic.Dictionary<string, List<int>> Hands = new();

    /// <summary>
    /// What the shuffle was seeded with. Worse than any single holding, because it reconstructs all
    /// of them at once. Set by the subclass at deal time; never transmitted.
    /// </summary>
    protected ulong DealSeed;

    /// <summary>
    /// Server-issued stamp for the current turn. A request quoting a stale one is dropped, which is
    /// what kills a double click, a click that lands after the turn already moved, and a replay.
    /// </summary>
    protected int TurnStamp;

    protected bool MatchRunning;

    [Signal]
    public delegate void ActionRejectedEventHandler(string reason);

    /// <summary>
    /// Correlates a refusal with the stamped turn that produced it. Existing games may keep using
    /// ActionRejected; latency-sensitive controllers use this companion signal so an old packet can
    /// never cancel input that belongs to a newer turn.
    /// </summary>
    [Signal]
    public delegate void StampedActionRejectedEventHandler(int turnToken, string reason);

    public override void Setup(TableGame tableGame)
    {
        Table = tableGame;

        Hands.Clear();
        DealSeed = 0;
        TurnStamp = 0;
        MatchRunning = false;

        ResetSecretState();

        if (!Multiplayer.IsServer())
            return;

        // The shared bridge rebuilds the mode for a late peer; this packet then catches its public
        // state up to the exact current turn.
        SignalUtil.ConnectGuarded(NetworkManager.Instance.NetworkProvider,
            NetworkProvider.SignalName.PlayerConnected,
            new Callable(this, MethodName.OnPeerConnected));
    }

    public override void _ExitTree()
    {
        var provider = NetworkManager.Instance?.NetworkProvider;
        if (provider != null)
        {
            SignalUtil.DisconnectGuarded(provider, NetworkProvider.SignalName.PlayerConnected,
                new Callable(this, MethodName.OnPeerConnected));
        }
    }

    public override void HandleNewTurnContext(Dictionary context) => ApplyPublicSnapshot(context);

    public override void HandleTurnExtensionContext(Dictionary context) => ApplyPublicSnapshot(context);

    public override void HandleMatchEnded()
    {
        MatchRunning = false;
        Hands.Clear();
        DealSeed = 0;
        ClearSecretState();
    }

    // ---------------------------------------------------------------- what a subclass supplies

    /// <summary>Wipe the game's own private state at setup.</summary>
    protected virtual void ResetSecretState() { }

    /// <summary>Wipe anything private the match leaves behind. Called when the match ends.</summary>
    protected virtual void ClearSecretState() { }

    /// <summary>Hand this peer's own holding to its game object.</summary>
    protected virtual void ApplyLocalHand(int[] items) { }

    /// <summary>Take a public snapshot into the game's public state.</summary>
    protected virtual void ApplyPublicSnapshot(Dictionary context) { }

    /// <summary>The public state as it stands, without moving the turn on.</summary>
    protected virtual Dictionary BuildSnapshot() => new();

    // ---------------------------------------------------------------- the shared turn gate

    /// <summary>
    /// The three checks every action shares: the match is live, it is this player's turn, and the
    /// request quotes the turn stamp the server is currently on.
    /// </summary>
    protected bool TurnIsOpenFor(string playerId, int turnToken, out List<int> hand, out string reason)
    {
        hand = null;

        if (!MatchRunning || Table == null)
        {
            reason = "match_not_running";
            return false;
        }

        if (!Table.IsTurnOwner(playerId))
        {
            reason = "not_your_turn";
            return false;
        }

        if (turnToken != TurnStamp)
        {
            reason = "stale_turn";
            return false;
        }

        if (!Hands.TryGetValue(playerId, out hand))
        {
            reason = "not_in_match";
            return false;
        }

        reason = null;
        return true;
    }

    // ---------------------------------------------------------------- private hand channel

    protected void SendHand(string playerId)
    {
        if (!Multiplayer.IsServer() || !Hands.TryGetValue(playerId, out var hand))
            return;

        if (!int.TryParse(playerId, out var peerId))
            return;

        var items = hand.ToArray();

        if (peerId == Multiplayer.GetUniqueId())
        {
            ApplyLocalHand(items);
            return;
        }

        // A seat with nobody actually connected to it cannot be sent to. Without this the server
        // logs an error per deal for every such seat — which is every seat but one in a headless
        // run, and a genuinely dropped player in a live one.
        if (!IsConnectedRemotePeer(peerId))
            return;

        RpcId(peerId, MethodName.ReceiveHand, items);
    }

    /// <summary>
    /// The one and only route a holding takes. Covers the deal, every draw and a reclaimed slot, so
    /// there is a single method to audit for the secrecy rule.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    protected void ReceiveHand(int[] itemIds)
    {
        ApplyLocalHand(itemIds);
    }

    protected void Reject(int requesterId, int turnToken, string reason)
    {
        if (requesterId == Multiplayer.GetUniqueId())
        {
            EmitSignal(SignalName.ActionRejected, reason);
            EmitSignal(SignalName.StampedActionRejected, turnToken, reason);
        }
        else if (IsConnectedRemotePeer(requesterId))
            RpcId(requesterId, MethodName.ReceiveActionRejected, turnToken, reason);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    protected void ReceiveActionRejected(int turnToken, string reason)
    {
        EmitSignal(SignalName.ActionRejected, reason);
        EmitSignal(SignalName.StampedActionRejected, turnToken, reason);
    }

    private bool IsConnectedRemotePeer(int peerId)
    {
        if (Multiplayer.MultiplayerPeer == null)
            return false;

        return ShouldSendRemoteReply(
            peerId, Multiplayer.GetUniqueId(), Multiplayer.GetPeers(), hasMultiplayerPeer: true);
    }

    internal static bool ShouldSendRemoteReply(
        int peerId, int localPeerId, int[] connectedPeers, bool hasMultiplayerPeer)
    {
        return hasMultiplayerPeer
            && peerId > 0
            && peerId != localPeerId
            && connectedPeers != null
            && System.Array.IndexOf(connectedPeers, peerId) >= 0;
    }

    // ---------------------------------------------------------------- late joiners and reconnects

    private void OnPeerConnected(int peerId)
    {
        if (!Multiplayer.IsServer() || !MatchRunning)
            return;

        RpcId(peerId, MethodName.ReceiveFullState, BuildSnapshot());
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    protected void ReceiveFullState(Dictionary context)
    {
        ApplyFullSnapshot(context);
    }

    /// <summary>
    /// Full reconnect snapshots may require presentation code to discard an interrupted local
    /// sequence. Ordinary turn contexts deliberately continue through ApplyPublicSnapshot.
    /// </summary>
    protected virtual void ApplyFullSnapshot(Dictionary context) => ApplyPublicSnapshot(context);

    /// <summary>Sends a reconnected peer its own holding and the whole public state.</summary>
    protected void ReissueTo(string newPlayerId)
    {
        if (!Multiplayer.IsServer())
            return;

        SendHand(newPlayerId);

        if (int.TryParse(newPlayerId, out var peerId) && peerId != Multiplayer.GetUniqueId())
            RpcId(peerId, MethodName.ReceiveFullState, BuildSnapshot());
    }
}
