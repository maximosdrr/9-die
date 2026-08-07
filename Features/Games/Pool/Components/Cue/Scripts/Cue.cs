using Godot;
using Godot.Collections;

[GlobalClass]
public partial class Cue : Node3D
{
    [Signal]
    public delegate void StrikeExecutedEventHandler(
        float aimYaw, float elevation, float speed, float tipOffsetX, float tipOffsetY);

    [ExportGroup("References")]
    [Export] public StateMachine StateMachine;
    [Export] public CueSfx CueSfx;
    [Export] public CueNetworkBridge StrokeNetworkBridge;
    [Export] public RayCast3D CueHandleSensor;

    [ExportGroup("Physics Config")]
    /// <summary>
    /// THE shot power knob: cue ball speed at a full-power stroke, in m/s. Response is linear, so
    /// this scales the whole range — half a draw is always half this speed.
    ///
    /// For reference: a normal shot is 1-4 m/s, a professional break about 8, and the record
    /// roughly 14. The old ForceMultiplier of 28 N·s on a 0.17 kg ball worked out to 165 m/s,
    /// which is why nothing on the table behaved plausibly.
    /// </summary>
    [Export] public float MaxCueBallSpeed = 8.0f;

    [Export] public float MinPowerThreshold = 0.02f;
    [Export] public float ElevationSensorMargin = 0.08f;

    [ExportGroup("Visual Config")]
    [Export] public float VisualGap = 0.01f;
    [Export] public float PostShotCooldown = 0.25f;

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

        StrokeNetworkBridge.Setup(this);

        SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
        SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));

        UpdateBallLimits();
        UpdateTurnState();
    }

    public override void _ExitTree()
    {
        if (PoolGame == null)
            return;

        SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
        SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
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
        EmitSignal(SignalName.StrikeExecuted, (float)shot.AimYaw, (float)shot.Elevation,
            (float)shot.Speed, (float)shot.TipOffsetX, (float)shot.TipOffsetY);

        var direction = -GlobalTransform.Basis.Z.Normalized();
        CueSfx.EmitStrikeSound(direction, (float)shot.Speed, new Vector3(SpinOffset.X, SpinOffset.Y, 0.0f));
        return true;
    }

    private Pool.Simulation.ShotInput BuildShotInput(float normalizedPower)
    {
        var direction = -GlobalTransform.Basis.Z.Normalized();

        var aimYaw = Mathf.Atan2(direction.X, direction.Z);
        var elevation = Mathf.Max(0.0f, -Mathf.Asin(Mathf.Clamp(direction.Y, -1.0f, 1.0f)));
        var speed = normalizedPower * MaxCueBallSpeed;

        // SpinOffset is in metres on the ball's face; the simulation wants it as a fraction of
        // the radius, which is what makes it independent of the ball's size.
        var offsetX = SpinOffset.X / CueBall.Radius;
        var offsetY = SpinOffset.Y / CueBall.Radius;

        return new Pool.Simulation.ShotInput(aimYaw, elevation, speed, offsetX, offsetY);
    }

    private void OnTurnChanged(string newPlayer, Dictionary context)
    {
        UpdateTurnState();
    }

    private void OnTurnExtended(Dictionary context)
    {
        UpdateTurnState();
    }

    private void UpdateTurnState()
    {
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
        if (PoolGame == null || PoolGame.TurnOwner == null)
            return false;

        var turnId = int.Parse((string)PoolGame.TurnOwner.Name);
        return turnId == Multiplayer.GetUniqueId();
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
        return IsInstanceValid(CueBall);
    }

    public void SnapToRestPose()
    {
        Position = new Vector3(SpinOffset.X, SpinOffset.Y, BallRadiusOffset);
    }
}
