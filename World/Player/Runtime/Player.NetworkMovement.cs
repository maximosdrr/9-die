using System.Collections.Generic;
using Godot;

/// <summary>
/// Captures owner intent, validates it on the server, simulates the authoritative body and
/// publishes disposable snapshots. Node authority remains input ownership, never transform trust.
/// </summary>
public partial class Player : CharacterBody3D
{
    internal const int MovementModeRequestsPerSecond = 4;
    internal const int MaximumTrackedMovementModePeers = 16;

    private readonly struct PredictionSample
    {
        public readonly int Sequence;
        public readonly Vector3 Position;

        public PredictionSample(int sequence, Vector3 position)
        {
            Sequence = sequence;
            Position = position;
        }
    }

    private const int MaximumPredictionSamples = 128;
    private PlayerMovementInputGuard _serverInput;
    private readonly List<PredictionSample> _predictionSamples = new();
    private int _nextInputSequence;
    private double _snapshotAccumulator;
    private Vector3 _snapshotPosition;
    private Vector3 _snapshotVelocity;
    private float _snapshotYaw;
    private bool _hasSnapshot;
    private readonly PlayerPredictionCorrection _predictionCorrection = new();
    private readonly PeerRequestRateLimiter _movementModeRequestLimiter = new(
        MovementModeRequestsPerSecond,
        windowMilliseconds: 1_000,
        maxTrackedPeers: MaximumTrackedMovementModePeers);

    /// <summary>
    /// The multiplayer authority still identifies which peer owns input, cameras and game views.
    /// It no longer grants permission to publish this body's transform.
    /// </summary>
    public bool UsesServerAuthoritativeMovement => true;

    public void ConfigureNetworkMovement(int owningPeerId)
    {
        Id = owningPeerId;
        _serverInput = new PlayerMovementInputGuard(Id, MaxInputPacketsPerSecond);
        _movementModeRequestLimiter.Clear();
    }

    private void InitializeNetworkMovement()
    {
        _serverInput ??= new PlayerMovementInputGuard(Id, MaxInputPacketsPerSecond);
        _snapshotPosition = GlobalPosition;
        _snapshotYaw = GlobalRotation.Y;
        SetPhysicsProcess(true);
    }

    public override void _PhysicsProcess(double delta)
    {
        var frameDelta = Mathf.Clamp((float)delta, 0.0f, 0.1f);
        if (frameDelta <= 0.0f)
            return;

        if (Multiplayer.IsServer())
        {
            ProcessServerMovement(frameDelta);
            return;
        }

        if (IsMultiplayerAuthority())
        {
            ProcessOwningClientMovement(frameDelta);
            return;
        }

        InterpolateRemoteSnapshot(frameDelta);
    }

    private void ProcessServerMovement(float delta)
    {
        var now = Time.GetTicksMsec();
        var canWalk = !IsInSeatedGameMode
            && (!IsMultiplayerAuthority() || CurrentControlState == ControllerStatesEnum.Player);

        if (IsMultiplayerAuthority() && canWalk)
        {
            var sequence = NextInputSequence();
            var intent = ReadWorldMovementIntent();
            _serverInput.TryAccept(Multiplayer.GetUniqueId(), sequence, intent,
                GlobalRotation.Y, now);
        }

        var timeoutMilliseconds = (ulong)Mathf.Max(1.0f, InputTimeoutSeconds * 1_000.0f);
        var activeIntent = canWalk
            ? _serverInput.ActiveIntent(now, timeoutMilliseconds)
            : Vector2.Zero;
        var targetYaw = _serverInput.HasAcceptedInput
            ? _serverInput.LatestYaw
            : GlobalRotation.Y;

        if (!canWalk)
            _serverInput.Stop(now);

        if (canWalk)
            SimulateWalking(activeIntent, targetYaw, delta, rotateAuthoritatively: true);
        else
            Velocity = Vector3.Zero;

        _snapshotAccumulator += delta;
        var snapshotInterval = 1.0 / Mathf.Clamp(SnapshotRate, 1.0f, 60.0f);
        if (_snapshotAccumulator < snapshotInterval)
            return;

        _snapshotAccumulator %= snapshotInterval;
        BroadcastAuthoritativeSnapshot();
    }

    private void ProcessOwningClientMovement(float delta)
    {
        ApplyPredictionCorrection(delta);

        if (IsInSeatedGameMode || CurrentControlState != ControllerStatesEnum.Player)
        {
            Velocity = Vector3.Zero;
            return;
        }

        var sequence = NextInputSequence();
        var intent = ReadWorldMovementIntent();
        var yaw = GlobalRotation.Y;
        SimulateWalking(intent, yaw, delta, rotateAuthoritatively: false);
        RememberPrediction(sequence);

        if (IsServerConnected())
        {
            RpcId(PlayerMovementProtocol.ServerPeerId, MethodName.SubmitMovementInput,
                sequence, intent, yaw);
        }
    }

    private void SimulateWalking(Vector2 worldIntent, float targetYaw, float delta,
        bool rotateAuthoritatively)
    {
        if (rotateAuthoritatively)
        {
            var maximumYawStep = Mathf.Max(0.0f, MaximumServerYawSpeed) * delta;
            var yawDifference = Mathf.Wrap(targetYaw - GlobalRotation.Y, -Mathf.Pi, Mathf.Pi);
            var yaw = GlobalRotation.Y + Mathf.Clamp(
                yawDifference, -maximumYawStep, maximumYawStep);
            GlobalRotation = new Vector3(0.0f, yaw, 0.0f);
        }

        var velocity = Velocity;

        if (!IsOnFloor())
            velocity.Y -= Gravity * delta;
        else
            velocity.Y = 0;

        if (worldIntent.LengthSquared() > 1e-6f)
        {
            velocity.X = worldIntent.X * Speed;
            velocity.Z = worldIntent.Y * Speed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(velocity.X, 0, Speed);
            velocity.Z = Mathf.MoveToward(velocity.Z, 0, Speed);
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    private Vector2 ReadWorldMovementIntent()
    {
        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        var worldDirection = GlobalBasis * new Vector3(input.X, 0.0f, input.Y);
        var intent = new Vector2(worldDirection.X, worldDirection.Z);
        return intent.LengthSquared() > 1.0f ? intent.Normalized() : intent;
    }

    private int NextInputSequence()
    {
        if (_nextInputSequence < int.MaxValue)
            _nextInputSequence++;
        return _nextInputSequence;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered,
        TransferChannel = PlayerMovementProtocol.TransferChannel)]
    private void SubmitMovementInput(int sequence, Vector2 worldIntent, float yaw)
    {
        if (!Multiplayer.IsServer())
            return;

        _serverInput ??= new PlayerMovementInputGuard(Id, MaxInputPacketsPerSecond);
        _serverInput.TryAccept(Multiplayer.GetRemoteSenderId(), sequence, worldIntent, yaw,
            Time.GetTicksMsec());
    }

    private void BroadcastAuthoritativeSnapshot()
    {
        if (!Multiplayer.IsServer() || Multiplayer.MultiplayerPeer == null)
            return;

        foreach (var peerId in Multiplayer.GetPeers())
        {
            RpcId(peerId, MethodName.ReceiveMovementSnapshot, _serverInput?.LastSequence ?? 0,
                GlobalPosition, GlobalRotation.Y, Velocity);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered,
        TransferChannel = PlayerMovementProtocol.TransferChannel)]
    private void ReceiveMovementSnapshot(int acknowledgedSequence, Vector3 position, float yaw,
        Vector3 velocity)
    {
        if (Multiplayer.IsServer()
            || Multiplayer.GetRemoteSenderId() != PlayerMovementProtocol.ServerPeerId
            || !PlayerMovementProtocol.IsValidSnapshot(position, yaw, velocity))
            return;

        if (IsMultiplayerAuthority())
        {
            ReconcilePrediction(acknowledgedSequence, position, yaw, velocity);
            return;
        }

        _snapshotPosition = position;
        _snapshotYaw = Mathf.Wrap(yaw, -Mathf.Pi, Mathf.Pi);
        _snapshotVelocity = velocity;

        if (_hasSnapshot && GlobalPosition.DistanceTo(position) <= HardCorrectionDistance)
            return;

        _hasSnapshot = true;
        SetVisualPose(position, _snapshotYaw, velocity);
    }

    private bool IsServerConnected() =>
        Multiplayer.MultiplayerPeer != null
        && System.Array.IndexOf(Multiplayer.GetPeers(), PlayerMovementProtocol.ServerPeerId) >= 0;

    /// <summary>
    /// Requests a seat/walking mode change. Coordinates are never sent: the server resolves the
    /// player's assigned seat or stand marker from its own scene and match state.
    /// </summary>
    public void RequestAuthoritativeMovementMode(bool walking)
    {
        if (!IsMultiplayerAuthority())
            return;

        if (Multiplayer.IsServer())
        {
            TryApplyRequestedMovementMode(Multiplayer.GetUniqueId(), walking);
            return;
        }

        if (IsServerConnected())
            RpcId(PlayerMovementProtocol.ServerPeerId,
                MethodName.RequestMovementModeOnServer, walking);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestMovementModeOnServer(bool walking)
    {
        if (!Multiplayer.IsServer())
            return;

        var requesterId = Multiplayer.GetRemoteSenderId();
        if (!_movementModeRequestLimiter.TryConsume(requesterId))
            return;

        TryApplyRequestedMovementMode(requesterId, walking);
    }

    internal bool TryConsumeMovementModeRequest(int peerId, ulong nowMilliseconds) =>
        _movementModeRequestLimiter.TryConsume(peerId, nowMilliseconds);

    internal int TrackedMovementModePeerCount => _movementModeRequestLimiter.TrackedPeerCount;

    private bool TryApplyRequestedMovementMode(int senderPeerId, bool walking)
    {
        if (!Multiplayer.IsServer()
            || !PlayerMovementProtocol.IsExpectedSender(senderPeerId, Id)
            || GameHandler?.CurrentController is not SeatedTableController controller
            || !controller.TryGetAuthoritativePose(!walking, out var position, out var yaw))
            return false;

        ApplyServerMovementMode(walking, position, yaw);
        return true;
    }

    internal void ApplyServerMovementMode(bool walking, Vector3 position, float yaw)
    {
        if (!Multiplayer.IsServer()
            || !PlayerMovementProtocol.IsValidSnapshot(position, yaw, Vector3.Zero))
            return;

        IsInSeatedGameMode = !walking;
        CurrentControlState = walking ? ControllerStatesEnum.Player : ControllerStatesEnum.Game;
        SetPhysicsProcess(walking);
        BodyCollision ??= GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        BodyCollision?.SetDeferred(CollisionShape3D.PropertyName.Disabled, !walking);
        _serverInput ??= new PlayerMovementInputGuard(Id, MaxInputPacketsPerSecond);
        _serverInput.Stop(Time.GetTicksMsec());
        SetVisualPose(position, yaw, Vector3.Zero);
        BroadcastAuthoritativeSnapshot();
    }
}
