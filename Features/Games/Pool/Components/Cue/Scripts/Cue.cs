using Godot;
using Godot.Collections;

[GlobalClass]
public partial class Cue : Node3D
{
	[Signal]
	public delegate void StrikeExecutedEventHandler(
		int sequence, float aimYaw, float elevation, float normalizedPower, float tipOffsetX, float tipOffsetY);
	[Signal]
	public delegate void StrikeRequestResolvedEventHandler(int sequence, bool accepted, string reason);

	[ExportGroup("References")]
	[Export] public StateMachine StateMachine;
	[Export] public CueSfx CueSfx;
	[Export] public CueNetworkBridge StrokeNetworkBridge;
	[Export] public RayCast3D CueHandleSensor;

	[ExportGroup("Physics Config")]
	/// <summary>
	/// Cue-stick speed used by the ordinary 0..80% range. Keeping this separate from the maximum
	/// preserves the calibrated feel of normal shots while reserving a harder top end for breaks.
	/// </summary>
	[Export] public float NormalCueSpeed = 3.5f;

	/// <summary>
	/// Cue-stick speed at 100% power. The final 20% of the meter eases from the normal calibration
	/// into this break speed; 80% therefore feels exactly as it did before.
	/// </summary>
	[Export] public float MaxCueSpeed = 5.5f;

	[Export] public float MinPowerThreshold = 0.02f;
	[Export] public float ElevationSensorMargin = 0.08f;

	[ExportGroup("Visual Config")]
	[Export] public float VisualGap = 0.01f;
	[Export] public float PostShotCooldown = 0.25f;
	[Export] public float FollowThroughDistance = 0.08f;
	[Export] public float FollowThroughDuration = 0.08f;
	[Export] public float FollowThroughVisibleTime = 0.14f;

	[ExportGroup("Jump Shot Config")]
	[Export] public float JumpMaxAngle = -65.0f;
	[Export] public float ElevationSensitivity = 2.0f;

	public PoolGame PoolGame;
	public Ball CueBall;
	public AimCameraPivot CameraPivot;

	/// <summary>Target pitch in degrees. The single owner of cue elevation: _Process eases the node's actual rotation toward it.</summary>
	public float CurrentElevation = 0.0f;

	public float MinSafeAngle = 0.0f;

	/// <summary>Target pitch in radians, already clamped by the obstacle limit.</summary>
	public float TargetElevationRad => Mathf.Min(Mathf.DegToRad(CurrentElevation), MinSafeAngle);

	/// <summary>How fast the cue eases to its target pitch, in e-folds per second.</summary>
	[Export] public float ElevationSharpness = 12.0f;

	/// <summary>0..1 draw currently held, for the power bar. Set by CueChargingState.</summary>
	public float ChargePower { get; private set; }

	public bool IsCharging { get; private set; }

	private int _nextShotSequence = 1;
	private int _pendingFeedbackSequence;
	private Vector3 _pendingStrikeDirection;
	private float _pendingStrikeSpeed;
	private Vector3 _pendingStrikeSpin;
	private Tween _shotVisibilityTween;

	[Signal] public delegate void ChargeChangedEventHandler(bool charging, float power);

	public void SetCharge(bool charging, float power)
	{
		if (IsCharging == charging && Mathf.IsEqualApprox(ChargePower, power))
			return;

		IsCharging = charging;
		ChargePower = power;
		EmitSignal(SignalName.ChargeChanged, charging, power);
	}

	public float BallRadiusOffset = 0.04f;
	public float SpinLimit = 0.02f;

	[Export] public Vector2 SpinOffset = Vector2.Zero;

	public void Setup(PoolGame poolGame, AimCameraPivot cameraPivot)
	{
		PoolGame = poolGame;
		CameraPivot = cameraPivot;
		CueBall = poolGame.CueBall;

		if (CueSfx != null)
			CueSfx.Cue = this;

		StrokeNetworkBridge.Setup(this);
		StrokeNetworkBridge.ShotRequestResolved += OnStrikeRequestResolved;

		SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
		SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
		SignalUtil.ConnectGuarded(PoolGame.SimulationRunner, PoolSimulationRunner.SignalName.ShotStarted, new Callable(this, MethodName.OnShotStarted));
		SignalUtil.ConnectGuarded(PoolGame.SimulationRunner, PoolSimulationRunner.SignalName.ShotFinished, new Callable(this, MethodName.OnShotFinished));

		UpdateBallLimits();
		UpdateTurnState();
	}

	public override void _ExitTree()
	{
		_shotVisibilityTween?.Kill();

		if (PoolGame == null)
			return;

		SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
		SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
		SignalUtil.DisconnectGuarded(PoolGame.SimulationRunner, PoolSimulationRunner.SignalName.ShotStarted, new Callable(this, MethodName.OnShotStarted));
		SignalUtil.DisconnectGuarded(PoolGame.SimulationRunner, PoolSimulationRunner.SignalName.ShotFinished, new Callable(this, MethodName.OnShotFinished));
		if (StrokeNetworkBridge != null)
			StrokeNetworkBridge.ShotRequestResolved -= OnStrikeRequestResolved;
	}

	public override void _Process(double delta)
	{
		// Exponential easing written so the rate is genuinely frame-rate independent. The old
		// `Lerp(current, target, 10 * delta)` is the linear approximation of this, which eases
		// measurably faster at low frame rates and never quite arrives.
		var blend = 1.0f - Mathf.Exp(-ElevationSharpness * (float)delta);

		var rot = Rotation;
		rot.X = Mathf.Lerp(rot.X, TargetElevationRad, blend);
		Rotation = rot;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!IsMultiplayerAuthority())
			return;

		UpdateSafeAngleLimit();
	}

	private void UpdateSafeAngleLimit()
	{
		if (CueHandleSensor == null || CameraPivot == null)
			return;

		var safeLimit = 0.0f;

		if (CueHandleSensor.IsColliding())
		{
			var collisionPoint = CueHandleSensor.GetCollisionPoint();
			var diffY = (collisionPoint.Y + ElevationSensorMargin) - CameraPivot.GlobalPosition.Y;

			if (diffY > 0)
			{
				var pivotPos2D = new Vector2(CameraPivot.GlobalPosition.X, CameraPivot.GlobalPosition.Z);
				var colPos2D = new Vector2(collisionPoint.X, collisionPoint.Z);
				var distanceToObstacle = pivotPos2D.DistanceTo(colPos2D);

				distanceToObstacle = Mathf.Max(distanceToObstacle, 0.1f);
				var angleRad = Mathf.Atan2(diffY, distanceToObstacle);

				safeLimit = -Mathf.Abs(angleRad);
			}
		}

		safeLimit = Mathf.Clamp(safeLimit, Mathf.DegToRad(-45.0f), 0.0f);
		MinSafeAngle = safeLimit;
	}

	/// <summary>
	/// Fires a shot from a normalised 0..1 power. Everything about the shot is described by
	/// angles and fractions rather than an impulse vector, so it survives the trip to the server
	/// intact and can be validated there — the old path sent a raw direction the server accepted
	/// unconditionally, which let a client aim straight down and jump past the elevation limit.
	/// </summary>
	public bool ExecuteStrikeWithPower(float normalizedPower)
	{
		if (!CanStrike())
			return false;

		if (normalizedPower <= MinPowerThreshold)
			return false;

		var shot = BuildShotInput(normalizedPower);

		// Always through the network bridge, host included: it is the single place the shot is
		// validated and the single place it is broadcast, so every peer plays the same one.
		var sequence = _nextShotSequence++;
		_pendingFeedbackSequence = sequence;
		_pendingStrikeDirection = -GlobalTransform.Basis.Z.Normalized();
		_pendingStrikeSpeed = (float)shot.Speed;
		_pendingStrikeSpin = new Vector3(SpinOffset.X, SpinOffset.Y, 0.0f);
		EmitSignal(SignalName.StrikeExecuted, sequence, (float)shot.AimYaw, (float)shot.Elevation,
			Mathf.Clamp(normalizedPower, 0.0f, 1.0f), (float)shot.TipOffsetX, (float)shot.TipOffsetY);
		return true;
	}

	private void OnStrikeRequestResolved(int sequence, bool accepted, string reason)
	{
		if (accepted && sequence == _pendingFeedbackSequence)
			CueSfx.EmitStrikeSound(_pendingStrikeDirection, _pendingStrikeSpeed, _pendingStrikeSpin);
		else if (!accepted)
			GD.PushWarning($"Tacada {sequence} rejeitada pelo servidor: {reason}.");

		if (sequence == _pendingFeedbackSequence)
			_pendingFeedbackSequence = 0;

		EmitSignal(SignalName.StrikeRequestResolved, sequence, accepted, reason);
	}

	private Pool.Simulation.ShotInput BuildShotInput(float normalizedPower)
	{
		var direction = -GlobalTransform.Basis.Z.Normalized();

		var aimYaw = Mathf.Atan2(direction.X, direction.Z);
		var elevation = Mathf.Max(0.0f, -Mathf.Asin(Mathf.Clamp(direction.Y, -1.0f, 1.0f)));
		var speed = PowerToCueSpeed(normalizedPower, NormalCueSpeed, MaxCueSpeed);

		// SpinOffset is in metres on the ball's face; the simulation wants it as a fraction of
		// the radius, which is what makes it independent of the ball's size.
		var offsetX = SpinOffset.X / CueBall.Radius;
		var offsetY = SpinOffset.Y / CueBall.Radius;

		return new Pool.Simulation.ShotInput(aimYaw, elevation, speed, offsetX, offsetY);
	}

	public static float PowerToCueSpeed(float normalizedPower, float normalCueSpeed, float maxCueSpeed)
	{
		var power = Mathf.Clamp(normalizedPower, 0.0f, 1.0f);
		var maximum = Mathf.Max(0.0f, maxCueSpeed);
		var normal = Mathf.Clamp(normalCueSpeed, 0.0f, maximum);
		var speed = power * normal;

		const float breakRangeStart = 0.8f;
		if (power <= breakRangeStart || maximum <= normal)
			return speed;

		var t = (power - breakRangeStart) / (1.0f - breakRangeStart);
		var smoothT = t * t * (3.0f - 2.0f * t);
		return Mathf.Min(maximum, speed + (maximum - normal) * smoothT);
	}

	private void OnTurnChanged(string newPlayer, Dictionary context)
	{
		UpdateTurnState();
	}

	private void OnTurnExtended(Dictionary context)
	{
		UpdateTurnState();
	}

	private void OnShotStarted()
	{
		if (!IsInsideTree())
			return;

		_shotVisibilityTween?.Kill();

		if (!IsMyTurn())
		{
			Hide();
			return;
		}

		Show();
		_shotVisibilityTween = CreateTween();
		_shotVisibilityTween.TweenInterval(Mathf.Max(0.0f, FollowThroughVisibleTime));
		_shotVisibilityTween.TweenCallback(Callable.From(Hide));
	}

	private void OnShotFinished()
	{
		// The resolver may finish the match and remove this controller while ShotFinished is still
		// dispatching to its remaining listeners. A detached node has no Multiplayer API anymore.
		if (!IsInsideTree())
			return;

		_shotVisibilityTween?.Kill();
		_shotVisibilityTween = null;

		if (IsMyTurn())
			Show();
	}

	private void UpdateTurnState()
	{
		if (!IsInsideTree() || !IsInstanceValid(StateMachine))
			return;

		CurrentElevation = 0.0f;

		if (IsMyTurn())
		{
			if (StateMachine.Current.Type == StatesRef.CueLocked)
				StateMachine.ChangeState(StatesRef.CueIdle, new Dictionary());
		}
		else
		{
			StateMachine.ChangeState(StatesRef.CueLocked, new Dictionary());
		}
	}

	private bool IsMyTurn()
	{
		return IsOwnedTurn(PoolGame, GetMultiplayerAuthority());
	}

	internal static bool IsOwnedTurn(PoolGame poolGame, int authorityId)
	{
		if (!IsInstanceValid(poolGame) || !IsInstanceValid(poolGame.TurnOwner))
			return false;

		return int.TryParse((string)poolGame.TurnOwner.Name, out var turnId)
			   && turnId == authorityId;
	}

	private void UpdateBallLimits()
	{
		if (CueBall == null)
			return;

		BallRadiusOffset = CueBall.Radius + VisualGap;
		SpinLimit = CueBall.Radius;
	}

	private bool CanStrike()
	{
		return IsInstanceValid(CueBall)
			   && IsMyTurn()
			   && PoolGame?.SimulationRunner != null
			   && !PoolGame.SimulationRunner.IsPlaying
			   && !IsTurnInputBlocked();
	}

	private bool IsTurnInputBlocked()
	{
		if (!IsInstanceValid(PoolGame))
			return true;

		if (IsInstanceValid(PoolGame.BallPlacementManager)
			&& PoolGame.BallPlacementManager.IsPlacementPendingFor(
				GetMultiplayerAuthority().ToString()))
			return true;

		var resolver = PoolGame.GameModeHandler?.CurrentGameMode?.TurnResolver as PoolTurnResolver;
		return resolver?.IsShotBlocked == true;
	}

	/// <summary>
	/// Completes the visual recovery without blindly locking the cue. A short shot can finish and
	/// extend the current player's turn before CueRecover's minimum cooldown expires. In that
	/// ordering there is no later turn signal to undo a lock, so the final state must be derived
	/// from the current turn and simulation instead of always becoming CueLocked.
	/// </summary>
	internal void CompletePostShotRecovery()
	{
		if (!IsInsideTree()
			|| !IsInstanceValid(StateMachine)
			|| StateMachine.Current?.Type != StatesRef.CueRecover)
			return;

		var shotIsStillPlaying = PoolGame?.SimulationRunner?.IsPlaying == true;
		var nextState = IsMyTurn() && !shotIsStillPlaying && !IsTurnInputBlocked()
			? StatesRef.CueIdle
			: StatesRef.CueLocked;

		StateMachine.ChangeState(nextState, new Dictionary());
	}

	public void SnapToRestPose()
	{
		Position = new Vector3(SpinOffset.X, SpinOffset.Y, BallRadiusOffset);
	}

	/// <summary>
	/// Restores presentation after ball placement returns input to this controller. This must be
	/// explicit: a peer can finish local shot playback before the authoritative turn update
	/// arrives and leave its cue hidden even though the controller itself becomes visible later.
	/// </summary>
	public void RestoreAimingPresentation()
	{
		_shotVisibilityTween?.Kill();
		_shotVisibilityTween = null;

		// A short scratch can finish while CueRecover's cooldown is still running. In solo the
		// same player keeps the turn, so that cooldown may switch the cue to CueLocked while ball
		// placement owns the input. Merely showing the cue afterwards leaves it unable to enter
		// the charging state. Taking control is the authoritative end of that transition: cancel
		// any recover/locked state and restore idle whenever this controller still owns the turn.
		if (IsInstanceValid(StateMachine) && StateMachine.Current != null && IsMyTurn())
			StateMachine.ChangeState(StatesRef.CueIdle, new Dictionary());

		SnapToRestPose();
		Show();
	}
}
