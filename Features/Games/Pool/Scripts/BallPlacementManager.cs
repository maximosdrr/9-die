using Godot;
using Godot.Collections;

[GlobalClass]
public partial class BallPlacementManager : Node
{
	[Signal]
	public delegate void PlacementFinishedEventHandler();

	private const float CollisionMargin = 0.001f;

	[ExportCategory("Configuration")]
	[Export] public RemoteTransform3D OverheadViewRemote;
	[Export] public Node3D TableOrigin;

	/// <summary>
	/// Height of the cloth above TableOrigin. Was 0.85 while the bed actually sits at 0.77, so
	/// ball-in-hand released the ball 13 cm up and let it bounce to wherever it liked — and the
	/// mouse projection plane was off by the same amount.
	/// </summary>
	[Export] public float TableSurfaceY = 0.77f;

	/// <summary>Set by PoolGame; the source of truth for play area and ball spacing.</summary>
	public PoolSimulationRunner SimulationRunner;

	private float BallRadius => (float)Pool.Simulation.BilliardConstants.Radius;
	private float PlayHalfWidth => SimulationRunner != null ? (float)SimulationRunner.Table.HalfWidth : 0.5088f;
	private float PlayHalfLength => SimulationRunner != null ? (float)SimulationRunner.Table.HalfLength : 1.0492f;

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

		// Resting height exactly — the ball is placed, not dropped.
		worldPos.Y = TableCenter.Y + TableSurfaceY + BallRadius;

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
		var limitX = PlayHalfWidth - BallRadius;
		var limitZ = PlayHalfLength - BallRadius;
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

		// A placed ball is simply at rest until the next shot is simulated. The freeze/wake/
		// nudge dance this used to do existed only to stop the rigid-body solver from either
		// falling asleep mid-placement or exploding out of an overlap; neither can happen now.
		ballNode.SetInPlay(true);
	}
}
