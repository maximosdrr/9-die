using System;
using Godot;
using Godot.Collections;

[GlobalClass]
public partial class AuthorityStateSynchronizer : Node
{
    internal const int ServerPeerId = 1;
    internal const int MaxStateTypeLength = 128;
    internal const int MaxMetadataEntries = 128;
    internal const int MaxMetadataCollectionItems = 1024;
    internal const int MaxMetadataDepth = 4;
    internal const int MaxMetadataStringLength = 512;
    internal const int MaxMetadataBytes = 16 * 1024;
    internal const int StateRequestsPerSecond = 12;

    public StateMachine StateMachine;
    public bool IsIncomingNetworkChange = false;

    private int _stateAuthorityId = ServerPeerId;
    private readonly PeerRequestRateLimiter _stateRequestLimiter = new(
        StateRequestsPerSecond,
        windowMilliseconds: 1_000,
        maxTrackedPeers: 16);

    public void Setup(StateMachine stateMachine)
    {
        StateMachine = stateMachine;
        _stateAuthorityId = StateMachine.GetMultiplayerAuthority();
        _stateRequestLimiter.Clear();

        // The state machine remains owned by the player that drives it, but this transport node is
        // always server-owned. That lets ApplyStateFromServer use RpcMode.Authority even for a
        // client-owned Player subtree. The original owner is retained above for request validation.
        SetMultiplayerAuthority(ServerPeerId);

        if (Multiplayer.IsServer())
            Multiplayer.PeerConnected += OnClientConnect;

        StateMachine.StateChanged += OnLocalStateChange;
    }

    public override void _ExitTree()
    {
        _stateRequestLimiter.Clear();
        if (StateMachine != null)
            StateMachine.StateChanged -= OnLocalStateChange;

        if (Multiplayer.IsServer())
            Multiplayer.PeerConnected -= OnClientConnect;
    }

    private void OnClientConnect(long peerId)
    {
        if (Multiplayer.IsServer())
            SendCurrentStateTo(peerId);
    }

    private void OnLocalStateChange(string type, Dictionary metadata)
    {
        if (IsIncomingNetworkChange || StateMachine == null)
            return;

        if (!StateMachine.IsMultiplayerAuthority())
            return;

        var safeMetadata = metadata ?? new Dictionary();
        if (!IsStatePayloadValid(type, safeMetadata))
        {
            GD.PushWarning($"State update '{type}' was not replicated because its payload is invalid.");
            return;
        }

        if (Multiplayer.IsServer())
            Rpc(MethodName.ApplyStateFromServer, type, safeMetadata);
        else
            RpcId(ServerPeerId, MethodName.RequestStateChangeOnServer, type, safeMetadata);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestStateChangeOnServer(string type, Dictionary metadata)
    {
        if (!Multiplayer.IsServer())
            return;

        var senderId = Multiplayer.GetRemoteSenderId();
        if (!_stateRequestLimiter.TryConsume(senderId))
            return;

        var safeMetadata = metadata ?? new Dictionary();

        if (senderId != _stateAuthorityId)
        {
            GD.PushWarning($"Peer {senderId} attempted to change a state owned by peer {_stateAuthorityId}.");
            SendCurrentStateTo(senderId);
            return;
        }

        if (!IsStatePayloadValid(type, safeMetadata))
        {
            GD.PushWarning($"Peer {senderId} sent an invalid state payload for '{type}'.");
            SendCurrentStateTo(senderId);
            return;
        }

        ApplyIncomingState(type, safeMetadata);
        Rpc(MethodName.ApplyStateFromServer, type, safeMetadata);
    }

    internal bool TryConsumeStateRequest(int peerId, ulong nowMilliseconds) =>
        _stateRequestLimiter.TryConsume(peerId, nowMilliseconds);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ApplyStateFromServer(string type, Dictionary metadata)
    {
        var senderId = Multiplayer.GetRemoteSenderId();
        var safeMetadata = metadata ?? new Dictionary();

        // RpcMode.Authority already enforces this at the engine boundary. Keep the explicit check so
        // the protocol remains safe if node authority is changed by a future scene refactor.
        if (senderId != ServerPeerId)
        {
            GD.PushWarning($"State update from non-server peer {senderId} was ignored.");
            return;
        }

        if (!IsStatePayloadValid(type, safeMetadata))
        {
            GD.PushWarning($"Server state payload for '{type}' was invalid and was ignored.");
            return;
        }

        ApplyIncomingState(type, safeMetadata);
    }

    private void ApplyIncomingState(string type, Dictionary metadata)
    {
        IsIncomingNetworkChange = true;
        try
        {
            StateMachine.ChangeState(type, metadata);
        }
        finally
        {
            IsIncomingNetworkChange = false;
        }
    }

    private void SendCurrentStateTo(long peerId)
    {
        if (!Multiplayer.IsServer() || StateMachine?.Current == null)
            return;

        var metadata = StateMachine.CurrentMetadata ?? new Dictionary();
        if (!IsStatePayloadValid(StateMachine.Current.Type, metadata))
        {
            GD.PushWarning($"Current state '{StateMachine.Current.Type}' has invalid metadata and was not synchronized.");
            return;
        }

        RpcId(peerId, MethodName.ApplyStateFromServer, StateMachine.Current.Type, metadata);
    }

    internal bool IsStatePayloadValid(string type, Dictionary metadata)
    {
        return StateMachine != null
               && !string.IsNullOrWhiteSpace(type)
               && type.Length <= MaxStateTypeLength
               && StateMachine.States.TryGetValue(type, out var state)
               && state != null
               && IsMetadataValid(metadata);
    }

    internal static bool IsMetadataValid(Dictionary metadata)
    {
        if (metadata == null)
            return true;

        var itemBudget = MaxMetadataCollectionItems;
        if (!TryValidateDictionary(metadata, 0, ref itemBudget))
            return false;

        try
        {
            return GD.VarToBytes(Variant.From(metadata)).Length <= MaxMetadataBytes;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryValidateDictionary(Dictionary dictionary, int depth, ref int itemBudget)
    {
        if (depth > MaxMetadataDepth
            || dictionary.Count > MaxMetadataEntries
            || !TryConsumeBudget(dictionary.Count, ref itemBudget))
        {
            return false;
        }

        foreach (Variant keyVariant in dictionary.Keys)
        {
            if (keyVariant.VariantType != Variant.Type.String)
                return false;

            var key = keyVariant.AsString();
            if (string.IsNullOrEmpty(key) || key.Length > MaxStateTypeLength)
                return false;

            if (!TryValidateVariant(dictionary[keyVariant], depth + 1, ref itemBudget))
                return false;
        }

        return true;
    }

    private static bool TryValidateVariant(Variant value, int depth, ref int itemBudget)
    {
        if (depth > MaxMetadataDepth)
            return false;

        switch (value.VariantType)
        {
            case Variant.Type.Object:
            case Variant.Type.Callable:
            case Variant.Type.Signal:
            case Variant.Type.Rid:
            case Variant.Type.NodePath:
                return false;

            case Variant.Type.String:
                return value.AsString().Length <= MaxMetadataStringLength;

            case Variant.Type.Dictionary:
                return TryValidateDictionary(value.AsGodotDictionary(), depth, ref itemBudget);

            case Variant.Type.Array:
                {
                    var array = value.AsGodotArray();
                    if (!TryConsumeBudget(array.Count, ref itemBudget))
                        return false;

                    foreach (Variant item in array)
                    {
                        if (!TryValidateVariant(item, depth + 1, ref itemBudget))
                            return false;
                    }

                    return true;
                }

            case Variant.Type.PackedByteArray:
                return TryConsumeBudget(value.AsByteArray().Length, ref itemBudget);
            case Variant.Type.PackedInt32Array:
                return TryConsumeBudget(value.AsInt32Array().Length, ref itemBudget);
            case Variant.Type.PackedInt64Array:
                return TryConsumeBudget(value.AsInt64Array().Length, ref itemBudget);
            case Variant.Type.PackedFloat32Array:
                return TryConsumeBudget(value.AsFloat32Array().Length, ref itemBudget);
            case Variant.Type.PackedFloat64Array:
                return TryConsumeBudget(value.AsFloat64Array().Length, ref itemBudget);
            case Variant.Type.PackedStringArray:
                {
                    var strings = value.AsStringArray();
                    if (!TryConsumeBudget(strings.Length, ref itemBudget))
                        return false;

                    foreach (var item in strings)
                    {
                        if (item.Length > MaxMetadataStringLength)
                            return false;
                    }

                    return true;
                }

            default:
                return true;
        }
    }

    private static bool TryConsumeBudget(int count, ref int itemBudget)
    {
        if (count < 0 || count > itemBudget)
            return false;

        itemBudget -= count;
        return true;
    }
}
