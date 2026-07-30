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
	[Export] public Node3D TableOrigin;
	[Export] public float TableSurfaceY = 0.85f;

	[ExportGroup("Table Limits")]
	[Export] public float PlayAreaWidth = 0.9f;
	[Export] public float PlayAreaLength = 1.8f;
	[Export] public float BallRadius = 0.029f;

	private Ball _ball;
	private Array<Ball> _otherBalls = new();
	private bool _isPlacing = false;
	private RemoteTransform3D _previousCameraRemote;

	private int _authorizedPlacerId = 0;
	private Ball _authorizedBall;

	public GlobalCamera Camera;

	private Vector3 TableCenter => TableOrigin != null ? TableOrigin.GlobalPosition : Vector3.Zero;

	public override void _Ready()
	{
		SetProcessUnhandledInput(false);
	}

	public void AuthorizePlacement(int playerId, Ball ball)
	{
		_authorizedPlacerId = playerId;
		_authorizedBall = ball;
	}

	public bool IsPlacementPendingFor(string playerId)
	{
		return _authorizedPlacerId != 0 && int.TryParse(playerId, out var id) && _authorizedPlacerId == id;
	}

	public void StartPlacement(Ball ballToPlace, Array<Ball> existingBalls)
	{
		if (!IsInstanceValid(ballToPlace) || Camera == null)
			return;

		_ball = ballToPlace;
		_otherBalls = existingBalls;
		_isPlacing = true;

		RequestPlacementState(_ball.GetPath(), true, _ball.GlobalPosition);
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

		worldPos.Y = TableCenter.Y + TableSurfaceY + BallRadius + SafetyHeightMargin;

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
			RequestPlacementState(_ball.GetPath(), false, _ball.GlobalPosition);

		SwitchCameraMode(false);
		_ball = null;
		EmitSignal(SignalName.PlacementFinished);
	}

	private Vector3 GetMouseProjectionOnTable(Vector2 screenPosition)
	{
		var rayOrigin = Camera.ProjectRayOrigin(screenPosition);
		var rayDir = Camera.ProjectRayNormal(screenPosition);

		var tablePlane = new Plane(Vector3.Up, TableCenter.Y + TableSurfaceY);
		var intersection = tablePlane.IntersectsRay(rayOrigin, rayDir);
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
		var center = TableCenter;

		pos.X = Mathf.Clamp(pos.X, center.X - limitX, center.X + limitX);
		pos.Z = Mathf.Clamp(pos.Z, center.Z - limitZ, center.Z + limitZ);
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
		if (active)
			InputFocus.Release();
		else
			InputFocus.Capture();
	}

	private void SwitchCameraMode(bool toOverhead)
	{
		if (Camera == null)
			return;

		if (toOverhead)
		{
			if (OverheadViewRemote != null)
			{
				_previousCameraRemote = Camera.CurrentRemote;
				Camera.TransitionTo(OverheadViewRemote);
			}
		}
		else
		{
			if (_previousCameraRemote != null)
				Camera.TransitionTo(_previousCameraRemote);
			_previousCameraRemote = null;
		}
	}

	private void RequestPlacementState(NodePath ballPath, bool isFrozen, Vector3 pos)
	{
		if (Multiplayer.IsServer())
			TryApplyPlacementState(Multiplayer.GetUniqueId(), ballPath, isFrozen, pos);
		else
			RpcId(1, MethodName.RequestSetBallPlacementState, ballPath, isFrozen, pos);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestSetBallPlacementState(NodePath ballPath, bool isFrozen, Vector3 pos)
	{
		if (!Multiplayer.IsServer())
			return;

		TryApplyPlacementState(Multiplayer.GetRemoteSenderId(), ballPath, isFrozen, pos);
	}

	private void TryApplyPlacementState(int requesterId, NodePath ballPath, bool isFrozen, Vector3 pos)
	{
		if (requesterId != _authorizedPlacerId)
			return;

		var requestedBall = GetNodeOrNull<Ball>(ballPath);
		if (requestedBall == null || requestedBall != _authorizedBall)
			return;

		var newAuthority = isFrozen ? requesterId : 1;
		Rpc(MethodName.SetBallPlacementState, ballPath, newAuthority, isFrozen, pos);

		if (!isFrozen)
		{
			_authorizedPlacerId = 0;
			_authorizedBall = null;
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
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
