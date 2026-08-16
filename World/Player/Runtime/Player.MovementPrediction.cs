using Godot;

/// <summary>
/// Positional correction that remains to be presented from the latest authoritative snapshot.
///
/// A snapshot is a newer estimate of the same player pose, not another movement impulse. Its
/// error therefore replaces any correction left by an older snapshot. Adding both errors makes
/// stale network state keep moving the player after its input has stopped.
///
/// Yaw deliberately does not live here. The owning peer's camera is direct local input; applying
/// server yaw back onto that same transform both moves the camera without mouse input and sends
/// the corrected value back as the next input. The server still validates and rate-limits yaw for
/// its authoritative body and for every observer. This is safe while movement is submitted in
/// world space and the walking collider is rotationally symmetric; gameplay-facing state should
/// use a separate authoritative body transform if either invariant changes.
/// </summary>
internal sealed class PlayerPredictionCorrection
{
    public Vector3 PositionError { get; private set; }

    public void Retarget(Vector3 positionError)
    {
        PositionError = positionError;
    }

    public Vector3 ConsumePositionStep(float maximumDistance)
    {
        var step = PositionError.LimitLength(Mathf.Max(0.0f, maximumDistance));
        PositionError -= step;
        if (PositionError.IsZeroApprox())
            PositionError = Vector3.Zero;
        return step;
    }

    public void Clear()
    {
        PositionError = Vector3.Zero;
    }
}

/// <summary>
/// Reconciles the owning client's predicted samples and interpolates server snapshots for
/// observers. This layer changes presentation only; collision authority stays on the server.
/// </summary>
public partial class Player : CharacterBody3D
{
    private bool _movementModeTransitionActive;
    private Vector3 _movementModeStartPosition;
    private Vector3 _movementModeTargetPosition;
    private float _movementModeStartYaw;
    private float _movementModeTargetYaw;
    private float _movementModeTransitionElapsed;
    private float _movementModeTransitionDuration;

    internal bool MovementModeTransitionActive => _movementModeTransitionActive;

    public override void _Process(double delta)
    {
        if (!_movementModeTransitionActive)
            return;

        _movementModeTransitionElapsed += Mathf.Clamp((float)delta, 0.0f, 0.1f);
        var linear = Mathf.Clamp(
            _movementModeTransitionElapsed / Mathf.Max(0.001f, _movementModeTransitionDuration),
            0.0f, 1.0f);
        var smooth = linear * linear * (3.0f - 2.0f * linear);

        GlobalPosition = _movementModeStartPosition.Lerp(_movementModeTargetPosition, smooth);
        GlobalRotation = new Vector3(0.0f,
            Mathf.LerpAngle(_movementModeStartYaw, _movementModeTargetYaw, smooth), 0.0f);
        Velocity = Vector3.Zero;

        if (linear < 1.0f)
            return;

        _movementModeTransitionActive = false;
        SetVisualPose(_movementModeTargetPosition, _movementModeTargetYaw, Vector3.Zero);
        SetProcess(false);

        if (Multiplayer.IsServer())
            BroadcastAuthoritativeSnapshot();
    }

    internal void BeginMovementModeTransition(Vector3 position, float yaw, float duration)
    {
        yaw = Mathf.Wrap(yaw, -Mathf.Pi, Mathf.Pi);

        // Setup and the owning client's request can resolve the same seat in quick succession on a
        // listen server. Do not restart an approach that is already travelling to that exact marker.
        if (_movementModeTransitionActive
            && _movementModeTargetPosition.DistanceSquaredTo(position) < 0.000001f
            && Mathf.Abs(Mathf.AngleDifference(_movementModeTargetYaw, yaw)) < 0.001f)
        {
            return;
        }

        if (duration <= 0.0f || GlobalPosition.DistanceSquaredTo(position) < 0.000001f)
        {
            _movementModeTransitionActive = false;
            SetProcess(false);
            SetVisualPose(position, yaw, Vector3.Zero);
            return;
        }

        _movementModeStartPosition = GlobalPosition;
        _movementModeTargetPosition = position;
        _movementModeStartYaw = GlobalRotation.Y;
        _movementModeTargetYaw = yaw;
        _movementModeTransitionElapsed = 0.0f;
        _movementModeTransitionDuration = duration;
        _movementModeTransitionActive = true;
        SetProcess(true);
    }

    internal void CancelMovementModeTransition()
    {
        _movementModeTransitionActive = false;
        SetProcess(false);
    }

    private void RememberPrediction(int sequence)
    {
        _predictionSamples.Add(new PredictionSample(sequence, GlobalPosition));
        if (_predictionSamples.Count > MaximumPredictionSamples)
            _predictionSamples.RemoveRange(0, _predictionSamples.Count - MaximumPredictionSamples);
    }

    private void ReconcilePrediction(int acknowledgedSequence, Vector3 serverPosition,
        float serverYaw, Vector3 serverVelocity)
    {
        // Snapshot rate is lower than the physics/input rate, so the server legitimately sends the
        // same acknowledgement more than once. Reprocessing it against today's predicted position
        // mistakes ordinary network latency for an error and pulls the owning camera backwards.
        if (acknowledgedSequence <= 0
            || acknowledgedSequence <= _lastAcknowledgedInputSequence)
        {
            return;
        }

        var comparedPosition = GlobalPosition;
        var removeCount = 0;
        var matchedPrediction = false;

        for (var index = 0; index < _predictionSamples.Count; index++)
        {
            var sample = _predictionSamples[index];
            if (sample.Sequence > acknowledgedSequence)
                break;

            removeCount = index + 1;
            if (sample.Sequence == acknowledgedSequence)
            {
                comparedPosition = sample.Position;
                matchedPrediction = true;
            }
        }

        if (removeCount > 0)
            _predictionSamples.RemoveRange(0, removeCount);

        _lastAcknowledgedInputSequence = acknowledgedSequence;

        // A sample can age out only after a long stall. Small differences are not actionable
        // without their matching historical pose; a teleport-sized discrepancy still fails safe.
        if (!matchedPrediction)
        {
            _predictionCorrection.Clear();
            if (GlobalPosition.DistanceTo(serverPosition) > HardCorrectionDistance)
                SetOwningClientPose(serverPosition, serverYaw, serverVelocity);
            return;
        }

        var alignedError = serverPosition - comparedPosition;
        if (alignedError.Length() > HardCorrectionDistance)
        {
            SetOwningClientPose(serverPosition, serverYaw, serverVelocity);
            _predictionCorrection.Clear();
            _predictionSamples.Clear();
            return;
        }

        _predictionCorrection.Retarget(alignedError);
    }

    private void ApplyPredictionCorrection(float delta)
    {
        if (!_predictionCorrection.PositionError.IsZeroApprox())
        {
            var step = _predictionCorrection.ConsumePositionStep(
                PredictionCorrectionSpeed * delta);
            MoveAndCollide(step);
        }
    }

    /// <summary>
    /// A hard position correction must not overwrite the owner's mouselook. Server yaw is used
    /// only as a fail-closed fallback if the local transform somehow became non-finite.
    /// </summary>
    private void SetOwningClientPose(Vector3 position, float serverYaw, Vector3 velocity)
    {
        var localYaw = PlayerMovementProtocol.ResolveOwningClientYaw(
            GlobalRotation.Y,
            serverYaw);
        SetVisualPose(position, localYaw, velocity);
    }

    private void InterpolateRemoteSnapshot(float delta)
    {
        if (!_hasSnapshot)
            return;

        if (GlobalPosition.DistanceTo(_snapshotPosition) > HardCorrectionDistance)
        {
            SetVisualPose(_snapshotPosition, _snapshotYaw, _snapshotVelocity);
            return;
        }

        var blend = 1.0f - Mathf.Exp(-Mathf.Max(0.0f, RemoteInterpolationSpeed) * delta);
        GlobalPosition = GlobalPosition.Lerp(_snapshotPosition, blend);
        var yaw = Mathf.LerpAngle(GlobalRotation.Y, _snapshotYaw, blend);
        GlobalRotation = new Vector3(0.0f, yaw, 0.0f);
        Velocity = _snapshotVelocity;
    }

    private void SetVisualPose(Vector3 position, float yaw, Vector3 velocity)
    {
        GlobalPosition = position;
        GlobalRotation = new Vector3(0.0f, Mathf.Wrap(yaw, -Mathf.Pi, Mathf.Pi), 0.0f);
        Velocity = velocity;
        ResetPhysicsInterpolation();
    }
}
