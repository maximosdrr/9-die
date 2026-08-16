using Godot;

/// <summary>Replicates bounded camera look so remote necks follow their owner's camera.</summary>
public partial class Player : CharacterBody3D
{
    private const int CameraLookChannel = 4;
    private const float CameraLookSendInterval = 1.0f / 20.0f;
    private const float CameraLookMinimumDelta = 0.0025f;
    private const float MaximumReplicatedLookRadians = 1.8f;
    internal const int CameraLookRequestsPerSecond = 30;
    internal const int MaximumTrackedCameraLookPeers = 16;
    private Vector2 _cameraLook;
    private Vector2 _lastSentCameraLook;
    private double _nextCameraLookSendTime;
    private readonly PeerRequestRateLimiter _cameraLookRequestLimiter = new(
        CameraLookRequestsPerSecond,
        windowMilliseconds: 1_000,
        maxTrackedPeers: MaximumTrackedCameraLookPeers);

    public void SetCameraLook(float yawRadians, float pitchRadians)
    {
        var look = SanitizeCameraLook(new Vector2(yawRadians, pitchRadians));
        ApplyCameraLook(look);

        if (!IsMultiplayerAuthority() || Multiplayer.MultiplayerPeer == null)
            return;

        var now = Time.GetTicksMsec() / 1000.0;
        // Mouse-motion events can arrive at the hardware polling rate (commonly 500/1000 Hz).
        // The previous AND condition bypassed this interval whenever the cursor moved far enough,
        // turning the advertised 20 Hz stream into one RPC per input event. Always enforce time
        // first; the delta threshold only decides whether a due sample is worth transmitting.
        if (!ShouldSendCameraLook(
                look,
                _lastSentCameraLook,
                now,
                _nextCameraLookSendTime))
        {
            return;
        }

        _lastSentCameraLook = look;
        _nextCameraLookSendTime = now + CameraLookSendInterval;

        if (Multiplayer.IsServer())
            BroadcastCameraLook(look);
        else if (IsServerConnected())
            RpcId(PlayerMovementProtocol.ServerPeerId,
                MethodName.SubmitCameraLook, look);
    }

    public void ResetCameraLook() => SetCameraLook(0.0f, 0.0f);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered,
        TransferChannel = CameraLookChannel)]
    private void SubmitCameraLook(Vector2 look)
    {
        var senderId = Multiplayer.GetRemoteSenderId();
        if (!Multiplayer.IsServer()
            || senderId != Id
            || !_cameraLookRequestLimiter.TryConsume(senderId)
            || !IsValidCameraLook(look))
        {
            return;
        }

        look = SanitizeCameraLook(look);
        ApplyCameraLook(look);
        BroadcastCameraLook(look);
    }

    private void BroadcastCameraLook(Vector2 look)
    {
        if (!Multiplayer.IsServer())
            return;

        foreach (var peerId in Multiplayer.GetPeers())
            RpcId(peerId, MethodName.ReceiveCameraLook, look);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered,
        TransferChannel = CameraLookChannel)]
    private void ReceiveCameraLook(Vector2 look)
    {
        if (Multiplayer.IsServer()
            || Multiplayer.GetRemoteSenderId() != PlayerMovementProtocol.ServerPeerId
            || !IsValidCameraLook(look))
        {
            return;
        }

        ApplyCameraLook(SanitizeCameraLook(look));
    }

    private void ApplyCameraLook(Vector2 look)
    {
        _cameraLook = look;
        CharacterVisual?.SetCameraLook(look.X, look.Y);
    }

    private static Vector2 SanitizeCameraLook(Vector2 look) => new(
        Mathf.Clamp(look.X, -MaximumReplicatedLookRadians, MaximumReplicatedLookRadians),
        Mathf.Clamp(look.Y, -MaximumReplicatedLookRadians, MaximumReplicatedLookRadians));

    private static bool IsValidCameraLook(Vector2 look) =>
        float.IsFinite(look.X)
        && float.IsFinite(look.Y)
        && Mathf.Abs(look.X) <= MaximumReplicatedLookRadians
        && Mathf.Abs(look.Y) <= MaximumReplicatedLookRadians;

    internal bool TryConsumeCameraLookRequest(int peerId, ulong nowMilliseconds) =>
        _cameraLookRequestLimiter.TryConsume(peerId, nowMilliseconds);

    internal static bool ShouldSendCameraLook(
        Vector2 look,
        Vector2 previousLook,
        double nowSeconds,
        double nextSendSeconds) =>
        nowSeconds >= nextSendSeconds
        && look.DistanceSquaredTo(previousLook)
            >= CameraLookMinimumDelta * CameraLookMinimumDelta;

    internal int TrackedCameraLookPeerCount => _cameraLookRequestLimiter.TrackedPeerCount;

    private void ClearCameraLookRequests() => _cameraLookRequestLimiter.Clear();
}
