using Godot;

/// <summary>
/// Reconciles the owning client's predicted samples and interpolates server snapshots for
/// observers. This layer changes presentation only; collision authority stays on the server.
/// </summary>
public partial class Player : CharacterBody3D
{
    private void RememberPrediction(int sequence)
    {
        _predictionSamples.Add(new PredictionSample(sequence, GlobalPosition, GlobalRotation.Y));
        if (_predictionSamples.Count > MaximumPredictionSamples)
            _predictionSamples.RemoveRange(0, _predictionSamples.Count - MaximumPredictionSamples);
    }

    private void ReconcilePrediction(int acknowledgedSequence, Vector3 serverPosition,
        float serverYaw, Vector3 serverVelocity)
    {
        var comparedPosition = GlobalPosition;
        var comparedYaw = GlobalRotation.Y;
        var removeCount = 0;

        for (var index = 0; index < _predictionSamples.Count; index++)
        {
            var sample = _predictionSamples[index];
            if (sample.Sequence > acknowledgedSequence)
                break;

            removeCount = index + 1;
            if (sample.Sequence == acknowledgedSequence)
            {
                comparedPosition = sample.Position;
                comparedYaw = sample.Yaw;
            }
        }

        if (removeCount > 0)
            _predictionSamples.RemoveRange(0, removeCount);

        var alignedError = serverPosition - comparedPosition;
        if (alignedError.Length() > HardCorrectionDistance)
        {
            SetVisualPose(serverPosition, serverYaw, serverVelocity);
            _predictionCorrection = Vector3.Zero;
            _predictionYawCorrection = 0.0f;
            _predictionSamples.Clear();
            return;
        }

        _predictionCorrection += alignedError;
        _predictionYawCorrection += Mathf.Wrap(serverYaw - comparedYaw, -Mathf.Pi, Mathf.Pi);
    }

    private void ApplyPredictionCorrection(float delta)
    {
        if (!_predictionCorrection.IsZeroApprox())
        {
            var step = _predictionCorrection.LimitLength(
                Mathf.Max(0.0f, PredictionCorrectionSpeed) * delta);
            MoveAndCollide(step);
            _predictionCorrection -= step;
        }

        if (Mathf.IsZeroApprox(_predictionYawCorrection))
            return;

        var yawStep = Mathf.Clamp(_predictionYawCorrection,
            -Mathf.Max(0.0f, PredictionYawCorrectionSpeed) * delta,
            Mathf.Max(0.0f, PredictionYawCorrectionSpeed) * delta);
        RotateY(yawStep);
        _predictionYawCorrection -= yawStep;
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
