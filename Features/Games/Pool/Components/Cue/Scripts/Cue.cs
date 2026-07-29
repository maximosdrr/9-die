using Godot;
using Godot.Collections;

[GlobalClass]
public partial class Cue : Node3D
{
    [Signal]
    public delegate void StrikeExecutedEventHandler(Vector3 direction, float force, Vector3 offset);

    [ExportGroup("References")]
    [Export] public StateMachine StateMachine;
    [Export] public CueSfx CueSfx;
    [Export] public CueNetworkBridge StrokeNetworkBridge;
    [Export] public RayCast3D CueHandleSensor;

    [ExportGroup("Physics Config")]
    [Export] public float MaxSpeedReference = 12.0f;
    [Export] public float ForceMultiplier = 8.0f;
    [Export] public float MinForceThreshold = 0.01f;
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

        PoolGame.TurnChanged += OnTurnChanged;
        PoolGame.TurnExtended += OnTurnExtended;

        UpdateBallLimits();
        UpdateTurnState();
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

        var force = CalculateImpulse(mouseSpeed);

        if (force <= MinForceThreshold)
            return false;

        var (direction, hitOffset) = GetStrikeVectors();

        if (Multiplayer.IsServer())
            CueBall.Strike(direction, force, hitOffset);
        else
            EmitSignal(SignalName.StrikeExecuted, direction, force, hitOffset);

        CueSfx.EmitStrikeSound(direction, force, hitOffset);
        return true;
    }

    private float CalculateImpulse(float inputSpeed)
    {
        var rawPower = Mathf.Clamp(inputSpeed / MaxSpeedReference, 0.0f, 1.0f);
        return Mathf.Pow(rawPower, 2.0f) * ForceMultiplier;
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

    private void OnTurnExtended()
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
