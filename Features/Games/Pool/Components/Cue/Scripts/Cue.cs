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
    [Export] public float MaxSpeedReference = 12.0f;

    /// <summary>
    /// Cue ball speed at full power, m/s. A professional break is about 8 m/s and the record is
    /// roughly 14 — the old ForceMultiplier of 28 N·s on a 0.17 kg ball worked out to 165 m/s,
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

    public float CurrentElevation = 0.0f;
    public float MinSafeAngle = 0.0f;

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
        var targetRotationRad = Mathf.Min(Mathf.DegToRad(CurrentElevation), MinSafeAngle);
        var rot = Rotation;
        rot.X = Mathf.Lerp(rot.X, targetRotationRad, 10.0f * (float)delta);
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

    public bool ExecuteStrike(float mouseSpeed)
    {
        if (!CanStrike())
            return false;

        var power = Mathf.Clamp(mouseSpeed / MaxSpeedReference, 0.0f, 1.0f);
        return ExecuteStrikeWithPower(power);
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

        if (Multiplayer.IsServer())
            PoolGame.SimulationRunner.ExecuteShot(shot);
        else
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

    private (Vector3 Direction, Vector3 HitOffset) GetStrikeVectors()
    {
        var dir = -GlobalTransform.Basis.Z.Normalized();
        var hitOffset = new Vector3(SpinOffset.X, SpinOffset.Y, 0.0f);

        return (dir, hitOffset);
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
