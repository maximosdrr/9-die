using Godot;
using Godot.Collections;

[GlobalClass]
public partial class BallPlacementManager : Node
{
	[Signal] public delegate void PlacementFinishedEventHandler();
	[Signal] public delegate void PlacementCommittedEventHandler();
	[Signal] public delegate void PlacementRejectedEventHandler(string reason);

	public enum PlacementRegion
	{
		FullTable,
		BehindHeadString,
	}

	private const float CollisionMargin = 0.001f;

	[ExportCategory("Configuration")]
	[Export] public RemoteTransform3D OverheadViewRemote;
	[Export] public Node3D TableOrigin;
	[Export] public float TableSurfaceY = 0.77f;

	/// <summary>Maximum preview updates sent by the placing player each second.</summary>
	[Export(PropertyHint.Range, "5,60,1")]
	public float PreviewUpdatesPerSecond = 24.0f;

	/// <summary>Set by PoolGame; the source of truth for play area and ball spacing.</summary>
	public PoolSimulationRunner SimulationRunner;

	private float BallRadius => (float)Pool.Simulation.BilliardConstants.Radius;
	private Ball _ball;
	private Array<Ball> _otherBalls = new();
	private bool _isPlacing;
	private RemoteTransform3D _previousCameraRemote;
	private Node3D _ghost;
	private Ball _previewBall;
	private bool _previewBallWasVisible;
	private ulong _lastPreviewSentAtMsec;
	private PlacementRegion _localPlacementRegion = PlacementRegion.FullTable;
	private float _localHeadStringZ;

	// These exist only on the server. A preview packet is accepted only while this exact player
	// is authorised to place this exact ball.
	private int _authorizedPlacerId;
	private Ball _authorizedBall;
	private ulong _lastServerPreviewAtMsec;
	private PlacementRegion _authorizedPlacementRegion = PlacementRegion.FullTable;
	private float _authorizedHeadStringZ;

	public GlobalCamera Camera;

	private Vector3 TableCenter => TableOrigin != null ? TableOrigin.GlobalPosition : Vector3.Zero;

	public override void _Ready()
	{
		SetProcessUnhandledInput(false);
	}

	public override void _ExitTree()
	{
		RemovePreviewVisual(restoreRealBall: true);
	}

	public void AuthorizePlacement(int playerId, Ball ball,
		PlacementRegion region = PlacementRegion.FullTable, float headStringZ = 0.0f)
	{
		if (!Multiplayer.IsServer())
			return;

		_authorizedPlacerId = playerId;
		_authorizedBall = ball;
		_authorizedPlacementRegion = region;
		_authorizedHeadStringZ = headStringZ;
		_lastServerPreviewAtMsec = 0;
	}

	public bool IsPlacementPendingFor(string playerId)
	{
		return _authorizedPlacerId != 0 && int.TryParse(playerId, out var id) && _authorizedPlacerId == id;
	}

	public void StartPlacement(Ball ballToPlace, Array<Ball> existingBalls,
		PlacementRegion region = PlacementRegion.FullTable, float headStringZ = 0.0f)
	{
		if (!IsInstanceValid(ballToPlace) || Camera == null || SimulationRunner == null)
			return;

		_ball = ballToPlace;
		_otherBalls = existingBalls;
		_isPlacing = true;
		_localPlacementRegion = region;
		_localHeadStringZ = headStringZ;
		_lastPreviewSentAtMsec = 0;

		var initialPosition = FindInitialPreviewPosition(ballToPlace);
		ShowPreviewVisual(ballToPlace, initialPosition);
		RequestPreviewStart(ballToPlace.GetPath(), initialPosition);

		SetInputActive(true);
		SwitchCameraMode(true);
		SetProcessUnhandledInput(true);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_isPlacing || !IsInstanceValid(_ball))
			return;

		if (@event is InputEventMouseMotion motion)
			ProcessPreviewMovement(motion.Position);
		else if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			RequestPlacementConfirmation();
	}

	private Vector3 FindInitialPreviewPosition(Ball ball)
	{
		if (TryValidatePlacement(ball.GlobalPosition, ball,
			_localPlacementRegion, _localHeadStringZ, out var valid))
			return valid;

		var preferred = SimulationRunner.ClampToPlayableArea(
			SimulationRunner.GlobalToTablePosition(ball.GlobalPosition));
		preferred = ClampToPlacementRegion(preferred, _localPlacementRegion, _localHeadStringZ);

		if (!SimulationRunner.TryFindNearestFreeSpot(preferred, ball, out var freeSpot))
			freeSpot = SimulationRunner.ClampToPlayableArea(Vector2.Zero);

		return SimulationRunner.TableToGlobalPosition(freeSpot);
	}

	private void ProcessPreviewMovement(Vector2 screenPosition)
	{
		var worldPosition = GetMouseProjectionOnTable(screenPosition);
		if (worldPosition == Vector3.Inf)
			return;

		var tablePosition = SimulationRunner.GlobalToTablePosition(worldPosition);
		tablePosition = ApplyCollisionSliding(tablePosition);
		tablePosition = SimulationRunner.ClampToPlayableArea(tablePosition);
		tablePosition = ClampToPlacementRegion(
			tablePosition, _localPlacementRegion, _localHeadStringZ);
		worldPosition = SimulationRunner.TableToGlobalPosition(tablePosition);

		SetPreviewPosition(worldPosition);

		var now = Time.GetTicksMsec();
		var interval = 1000.0f / Mathf.Max(PreviewUpdatesPerSecond, 1.0f);
		if (_lastPreviewSentAtMsec != 0 && now - _lastPreviewSentAtMsec < interval)
			return;

		_lastPreviewSentAtMsec = now;
		RequestPreviewMove(_ball.GetPath(), worldPosition);
	}

	private void RequestPlacementConfirmation()
	{
		if (!IsInstanceValid(_ghost) || !IsInstanceValid(_ball))
			return;

		var proposedPosition = _ghost.GlobalPosition;
		if (!TryValidatePlacement(proposedPosition, _ball,
			_localPlacementRegion, _localHeadStringZ, out _))
		{
			HandlePlacementRejected("invalid_position");
			return;
		}

		if (Multiplayer.IsServer())
			TryConfirmPlacement(Multiplayer.GetUniqueId(), _ball.GetPath(), proposedPosition);
		else
			RpcId(1, MethodName.RequestConfirmPlacement, _ball.GetPath(), proposedPosition);
	}

	private Vector3 GetMouseProjectionOnTable(Vector2 screenPosition)
	{
		var rayOrigin = Camera.ProjectRayOrigin(screenPosition);
		var rayDirection = Camera.ProjectRayNormal(screenPosition);

		var anchor = SimulationRunner?.TableAnchor;
		var planeNormal = anchor != null ? anchor.GlobalBasis.Y.Normalized() : Vector3.Up;
		var planePoint = anchor != null ? anchor.GlobalPosition : TableCenter + Vector3.Up * TableSurfaceY;
		var tablePlane = new Plane(planeNormal, planePoint);
		var intersection = tablePlane.IntersectsRay(rayOrigin, rayDirection);
		return intersection ?? Vector3.Inf;
	}

	private Vector2 ApplyCollisionSliding(Vector2 proposedPosition)
	{
		var currentPosition = proposedPosition;
		var minimumSeparation = BallRadius * 2.0f + CollisionMargin;

		foreach (var otherBall in _otherBalls)
		{
			if (!IsInstanceValid(otherBall) || otherBall == _ball || !otherBall.InPlay)
				continue;

			var obstaclePosition = SimulationRunner.GlobalToTablePosition(otherBall.GlobalPosition);
			if (currentPosition.DistanceTo(obstaclePosition) >= minimumSeparation)
				continue;

			var direction = (currentPosition - obstaclePosition).Normalized();
			if (direction == Vector2.Zero)
				direction = Vector2.Right;

			currentPosition = obstaclePosition + direction * minimumSeparation;
		}

		return currentPosition;
	}

	private void CompleteLocalPlacement()
	{
		if (!_isPlacing)
			return;

		_isPlacing = false;
		SetProcessUnhandledInput(false);
		SetInputActive(false);
		SwitchCameraMode(false);
		_ball = null;
		_otherBalls.Clear();
		_localPlacementRegion = PlacementRegion.FullTable;
		_localHeadStringZ = 0.0f;
		EmitSignal(SignalName.PlacementFinished);
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
			if (OverheadViewRemote == null)
				return;

			_previousCameraRemote = Camera.CurrentRemote;
			Camera.TransitionTo(OverheadViewRemote);
			return;
		}

		if (_previousCameraRemote != null)
			Camera.TransitionTo(_previousCameraRemote);
		_previousCameraRemote = null;
	}

	private void RequestPreviewStart(NodePath ballPath, Vector3 position)
	{
		if (Multiplayer.IsServer())
			TryStartPreview(Multiplayer.GetUniqueId(), ballPath, position);
		else
			RpcId(1, MethodName.RequestStartPlacementPreview, ballPath, position);
	}

	private void RequestPreviewMove(NodePath ballPath, Vector3 position)
	{
		if (Multiplayer.IsServer())
			TryMovePreview(Multiplayer.GetUniqueId(), ballPath, position);
		else
			RpcId(1, MethodName.RequestMovePlacementPreview, ballPath, position);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestStartPlacementPreview(NodePath ballPath, Vector3 position)
	{
		if (Multiplayer.IsServer())
			TryStartPreview(Multiplayer.GetRemoteSenderId(), ballPath, position);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
		TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
	private void RequestMovePlacementPreview(NodePath ballPath, Vector3 position)
	{
		if (Multiplayer.IsServer())
			TryMovePreview(Multiplayer.GetRemoteSenderId(), ballPath, position);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestConfirmPlacement(NodePath ballPath, Vector3 position)
	{
		if (Multiplayer.IsServer())
			TryConfirmPlacement(Multiplayer.GetRemoteSenderId(), ballPath, position);
	}

	private bool TryGetAuthorizedBall(int requesterId, NodePath ballPath, out Ball requestedBall)
	{
		requestedBall = null;
		if (requesterId != _authorizedPlacerId)
			return false;

		requestedBall = GetNodeOrNull<Ball>(ballPath);
		return requestedBall != null && requestedBall == _authorizedBall;
	}

	private void TryStartPreview(int requesterId, NodePath ballPath, Vector3 position)
	{
		if (!TryGetAuthorizedBall(requesterId, ballPath, out var requestedBall))
		{
			RejectPlacement(requesterId, "not_authorized");
			return;
		}

		if (!TryValidatePlacement(position, requestedBall,
			_authorizedPlacementRegion, _authorizedHeadStringZ, out var validatedPosition))
		{
			var preferred = SimulationRunner.ClampToPlayableArea(
				SimulationRunner.GlobalToTablePosition(position));
			preferred = ClampToPlacementRegion(
				preferred, _authorizedPlacementRegion, _authorizedHeadStringZ);
			if (!SimulationRunner.TryFindNearestFreeSpot(preferred, requestedBall, out var freeSpot))
			{
				RejectPlacement(requesterId, "no_free_position");
				return;
			}

			freeSpot = ClampToPlacementRegion(
				freeSpot, _authorizedPlacementRegion, _authorizedHeadStringZ);
			validatedPosition = SimulationRunner.TableToGlobalPosition(freeSpot);
			if (!TryValidatePlacement(validatedPosition, requestedBall,
				_authorizedPlacementRegion, _authorizedHeadStringZ, out validatedPosition))
			{
				RejectPlacement(requesterId, "no_free_position");
				return;
			}
		}

		Rpc(MethodName.SetPlacementPreview, ballPath, true, validatedPosition);
		_lastServerPreviewAtMsec = Time.GetTicksMsec();
	}

	private void TryMovePreview(int requesterId, NodePath ballPath, Vector3 position)
	{
		if (!TryGetAuthorizedBall(requesterId, ballPath, out var requestedBall))
			return;

		var now = Time.GetTicksMsec();
		var minimumInterval = 1000.0f / Mathf.Max(PreviewUpdatesPerSecond * 1.5f, 1.0f);
		if (_lastServerPreviewAtMsec != 0 && now - _lastServerPreviewAtMsec < minimumInterval)
			return;

		if (!TryValidatePlacement(position, requestedBall,
			_authorizedPlacementRegion, _authorizedHeadStringZ, out var validatedPosition))
			return;

		_lastServerPreviewAtMsec = now;
		Rpc(MethodName.UpdatePlacementPreview, ballPath, validatedPosition);
	}

	private void TryConfirmPlacement(int requesterId, NodePath ballPath, Vector3 position)
	{
		if (!TryGetAuthorizedBall(requesterId, ballPath, out var requestedBall))
		{
			RejectPlacement(requesterId, "not_authorized");
			return;
		}

		if (!TryValidatePlacement(position, requestedBall,
			_authorizedPlacementRegion, _authorizedHeadStringZ, out var validatedPosition))
		{
			RejectPlacement(requesterId, "invalid_position");
			return;
		}

		_authorizedPlacerId = 0;
		_authorizedBall = null;
		_authorizedPlacementRegion = PlacementRegion.FullTable;
		_authorizedHeadStringZ = 0.0f;
		_lastServerPreviewAtMsec = 0;
		Rpc(MethodName.CommitPlacement, ballPath, validatedPosition);
	}

	private void RejectPlacement(int requesterId, string reason)
	{
		if (requesterId == Multiplayer.GetUniqueId())
			HandlePlacementRejected(reason);
		else
			RpcId(requesterId, MethodName.PlacementWasRejected, reason);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SetPlacementPreview(NodePath ballPath, bool active, Vector3 position)
	{
		var ballNode = GetNodeOrNull<Ball>(ballPath);
		if (ballNode == null)
			return;

		if (!active)
		{
			RemovePreviewVisual(restoreRealBall: true);
			return;
		}

		SimulationRunner?.StopPresentationForBall(ballNode);
		ShowPreviewVisual(ballNode, position);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
	private void UpdatePlacementPreview(NodePath ballPath, Vector3 position)
	{
		var ballNode = GetNodeOrNull<Ball>(ballPath);
		if (ballNode == null || ballNode != _previewBall)
			return;

		// The placing player already applies the cursor locally at full frame rate. Ignoring the
		// echoed packet prevents network latency from pulling their ghost backwards; spectators
		// still consume the server-relayed update.
		if (_isPlacing && ballNode == _ball)
			return;

		SetPreviewPosition(position);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void CommitPlacement(NodePath ballPath, Vector3 finalPosition)
	{
		var ballNode = GetNodeOrNull<Ball>(ballPath);
		if (ballNode == null)
			return;

		SimulationRunner?.StopPresentationForBall(ballNode);
		RemovePreviewVisual(restoreRealBall: false);
		ballNode.GlobalPosition = finalPosition;
		ballNode.SetInPlay(true);
		EmitSignal(SignalName.PlacementCommitted);

		if (_isPlacing && ballNode == _ball)
			CompleteLocalPlacement();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PlacementWasRejected(string reason)
	{
		HandlePlacementRejected(reason);
	}

	private void HandlePlacementRejected(string reason)
	{
		GD.PushWarning($"Posicionamento da bola rejeitado pelo servidor: {reason}.");
		EmitSignal(SignalName.PlacementRejected, reason);
	}

	private void ShowPreviewVisual(Ball ball, Vector3 position)
	{
		if (_previewBall != ball || !IsInstanceValid(_ghost))
		{
			RemovePreviewVisual(restoreRealBall: true);
			_previewBall = ball;
			_previewBallWasVisible = ball.Visible;
			_ghost = ball.CreatePlacementGhost();
			ball.GetParent().AddChild(_ghost);
		}

		// Reassert on every preview packet as a safety net against any presentation update that
		// may have changed visibility between network frames.
		ball.SetPlacementPreviewActive(true);
		SetPreviewPosition(position);
	}

	private void SetPreviewPosition(Vector3 position)
	{
		if (IsInstanceValid(_ghost))
			_ghost.GlobalPosition = position;
	}

	private void RemovePreviewVisual(bool restoreRealBall)
	{
		if (IsInstanceValid(_ghost))
			_ghost.QueueFree();

		if (IsInstanceValid(_previewBall))
		{
			_previewBall.SetPlacementPreviewActive(false);
			if (restoreRealBall)
				_previewBall.Visible = _previewBallWasVisible;
		}

		_ghost = null;
		_previewBall = null;
		_previewBallWasVisible = false;
	}

	public bool TryValidatePlacement(Vector3 requestedGlobalPosition, Ball ball,
		PlacementRegion region, float headStringZ, out Vector3 validatedGlobalPosition)
	{
		validatedGlobalPosition = Vector3.Zero;
		if (SimulationRunner == null
			|| !SimulationRunner.TryValidatePlacement(
				requestedGlobalPosition, ball, out validatedGlobalPosition))
			return false;

		var tablePosition = SimulationRunner.GlobalToTablePosition(validatedGlobalPosition);
		return IsInsidePlacementRegion(tablePosition, region, headStringZ);
	}

	public static bool IsInsidePlacementRegion(
		Vector2 tablePosition, PlacementRegion region, float headStringZ)
	{
		return region != PlacementRegion.BehindHeadString
		       || tablePosition.Y <= headStringZ + 0.00001f;
	}

	private static Vector2 ClampToPlacementRegion(
		Vector2 tablePosition, PlacementRegion region, float headStringZ)
	{
		if (region == PlacementRegion.BehindHeadString)
			tablePosition.Y = Mathf.Min(tablePosition.Y, headStringZ);

		return tablePosition;
	}
}
