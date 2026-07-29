using Godot;
using Godot.Collections;

[GlobalClass]
public partial class BallPlacementManager : Node
{
    [Signal]
    public delegate void PlacementFinishedEventHandler();

    private const float CollisionMargin = 0.001f;
    private const float SafetyHeightMargin = 0.05f;
    private const float WakeImpulseStrength = 0.05f;
    private const float SleepDelaySeconds = 0.2f;

    [ExportCategory("Configuration")]
    [Export] public RemoteTransform3D OverheadViewRemote;
    [Export] public float TableSurfaceY = 0.85f;

    [ExportGroup("Table Limits")]
    [Export] public float PlayAreaWidth = 0.9f;
    [Export] public float PlayAreaLength = 1.8f;
    [Export] public float BallRadius = 0.029f;

    private Ball _ball;
    private Array<Ball> _otherBalls = new();
    private bool _isPlacing = false;
    private Plane _tablePlane;
    private RemoteTransform3D _previousCameraRemote;

    public override void _Ready()
    {
        _tablePlane = new Plane(Vector3.Up, TableSurfaceY);
        SetProcessUnhandledInput(false);
    }

    public void StartPlacement(Ball ballToPlace, Array<Ball> existingBalls)
    {
        if (!IsInstanceValid(ballToPlace) || Global.Instance.Camera == null)
            return;

        _ball = ballToPlace;
        _otherBalls = existingBalls;
        _isPlacing = true;

        Rpc(MethodName.SetBallPlacementState, _ball.GetPath(), Multiplayer.GetUniqueId(), true, _ball.GlobalPosition);
        SetInputActive(true);
        SwitchCameraMode(true);
        SetProcessUnhandledInput(true);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_isPlacing || !IsInstanceValid(_ball))
            return;

        if (@event is InputEventMouseMotion motion)
        {
            ProcessBallMovement(motion.Position);
        }
        else if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            TryConfirmPlacement();
        }
    }

    private void ProcessBallMovement(Vector2 screenPosition)
    {
        var worldPos = GetMouseProjectionOnTable(screenPosition);
        if (worldPos == Vector3.Inf)
            return;

        worldPos.Y = TableSurfaceY + BallRadius + SafetyHeightMargin;

        worldPos = ApplyCollisionSliding(worldPos);
        worldPos = ClampPositionToTable(worldPos);

        _ball.GlobalPosition = worldPos;
    }

    private void TryConfirmPlacement()
    {
        if (CheckForOverlaps())
            return;

        FinishPlacement();
    }

    private void FinishPlacement()
    {
        _isPlacing = false;
        SetProcessUnhandledInput(false);
        SetInputActive(false);

        if (IsInstanceValid(_ball))
            Rpc(MethodName.SetBallPlacementState, _ball.GetPath(), 1, false, _ball.GlobalPosition);

        SwitchCameraMode(false);
        _ball = null;
        EmitSignal(SignalName.PlacementFinished);
    }

    private Vector3 GetMouseProjectionOnTable(Vector2 screenPosition)
    {
        var camera = Global.Instance.Camera;
        var rayOrigin = camera.ProjectRayOrigin(screenPosition);
        var rayDir = camera.ProjectRayNormal(screenPosition);

        var intersection = _tablePlane.IntersectsRay(rayOrigin, rayDir);
        if (intersection == null)
            return Vector3.Inf;

        return intersection.Value;
    }

    private Vector3 ApplyCollisionSliding(Vector3 proposedPos)
    {
        var currentPos = proposedPos;
        var minSeparationDist = (BallRadius * 2.0f) + CollisionMargin;

        foreach (var otherBall in _otherBalls)
        {
            if (!IsValidObstacle(otherBall))
                continue;

            var obstaclePos = otherBall.GlobalPosition;
            obstaclePos.Y = currentPos.Y;

            var distance = currentPos.DistanceTo(obstaclePos);

            if (distance < minSeparationDist)
            {
                var direction = (currentPos - obstaclePos).Normalized();
                if (direction == Vector3.Zero)
                    direction = Vector3.Right;

                currentPos = obstaclePos + (direction * minSeparationDist);
            }
        }

        return currentPos;
    }

    private Vector3 ClampPositionToTable(Vector3 pos)
    {
        var limitX = (PlayAreaWidth * 0.5f) - BallRadius;
        var limitZ = (PlayAreaLength * 0.5f) - BallRadius;

        pos.X = Mathf.Clamp(pos.X, -limitX, limitX);
        pos.Z = Mathf.Clamp(pos.Z, -limitZ, limitZ);
        return pos;
    }

    private bool CheckForOverlaps()
    {
        var minDist = (BallRadius * 2.0f) - CollisionMargin;

        foreach (var otherBall in _otherBalls)
        {
            if (!IsValidObstacle(otherBall))
                continue;

            if (_ball.GlobalPosition.DistanceTo(otherBall.GlobalPosition) < minDist)
                return true;
        }

        return false;
    }

    private bool IsValidObstacle(Ball otherBall)
    {
        return IsInstanceValid(otherBall) && otherBall != _ball;
    }

    private void SetInputActive(bool active)
    {
        Input.MouseMode = active ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
    }

    private void SwitchCameraMode(bool toOverhead)
    {
        if (Global.Instance.Camera == null)
            return;

        if (toOverhead)
        {
            if (OverheadViewRemote != null)
            {
                _previousCameraRemote = Global.Instance.Camera.CurrentRemote;
                Global.Instance.Camera.TransitionTo(OverheadViewRemote);
            }
        }
        else
        {
            if (_previousCameraRemote != null)
                Global.Instance.Camera.TransitionTo(_previousCameraRemote);
            _previousCameraRemote = null;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SetBallPlacementState(NodePath ballPath, int newAuthority, bool isFrozen, Vector3 finalPos)
    {
        var ballNode = GetNodeOrNull<Ball>(ballPath);
        if (ballNode == null)
            return;

        ballNode.GlobalPosition = finalPos;
        ballNode.SetMultiplayerAuthority(newAuthority);

        var synchronizer = ballNode.MultiplayerSynchronizerNode;
        if (synchronizer != null)
            synchronizer.SetMultiplayerAuthority(newAuthority);

        ballNode.Freeze = isFrozen;
        ballNode.LinearVelocity = Vector3.Zero;
        ballNode.AngularVelocity = Vector3.Zero;

        if (!isFrozen)
            WakeUpBall(ballNode);
    }

    private void WakeUpBall(Ball ballNode)
    {
        ballNode.CanSleep = false;
        ballNode.Sleeping = false;

        if (ballNode.IsMultiplayerAuthority())
        {
            ballNode.ApplyCentralImpulse(Vector3.Down * WakeImpulseStrength);

            var timer = GetTree().CreateTimer(SleepDelaySeconds, false);
            timer.Timeout += () =>
            {
                if (IsInstanceValid(ballNode))
                    ballNode.CanSleep = true;
            };
        }
    }
}
