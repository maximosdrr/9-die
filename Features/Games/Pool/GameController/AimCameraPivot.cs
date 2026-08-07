using Godot;
using System.Threading.Tasks;

[GlobalClass]
public partial class AimCameraPivot : Node3D
{
    public Node3D ElevationNode;

    [ExportGroup("Camera Behavior")]
    [Export] public float MouseSensitivity = 0.0015f;
    [Export] public float DistanceFromBall = 0.55f;
    [Export] public float HeightOffset = 0.1f;

    /// <summary>
    /// How fast the pivot closes on the cue ball, in e-folds per second. This used to be a tween
    /// fired at four discrete moments, which snapshotted the ball's position the instant it
    /// started — so a ball that was still creeping, or one being placed by hand, left the cue
    /// parked somewhere the ball no longer was.
    /// </summary>
    [Export] public float FollowSharpness = 10.0f;

    [ExportGroup("Rotation Limits")]
    [Export] public float LimitCeilingDeg = -90.0f;
    [Export] public float LimitFloorDeg = 15.0f;
    [Export] public float MaxNeckLookUpDeg = -20.0f;

    [Export] public float CueOffsetDeg = -5.0f;

    [ExportGroup("Player Positioning")]
    [Export] public float PlayerOrbitDistance = 1.2f;
    [Export] public float PlayerFloorHeight = 0.0f;

    public Cue Cue;
    private float _rotY = 0.0f;
    private float _rotX = 0.0f;
    private float _headAngle = 0.0f;
    private Node3D _cameraNode;

    public Ball Target;
    public PoolGame PoolGame;
    public Player Player;

    public override void _Ready()
    {
        ElevationNode = GetNode<Node3D>("Elevation");
        TopLevel = true;
        InitializePositions();
    }

    public async Task Setup(PoolGame poolGame, PoolController poolController)
    {
        Target = poolGame.CueBall;
        PoolGame = poolGame;
        Cue = poolController.Cue;
        Player = poolController.Player;

        ConnectSignals();

        if (Target != null)
        {
            await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
            GlobalPosition = Target.GlobalPosition;
        }
    }

    public override void _Process(double delta)
    {
        FollowCueBall(delta);
    }

    /// <summary>
    /// Rides the cue ball every frame, except while a shot is playing back — during the shot the
    /// player watches from where they aimed rather than being dragged around the table, and the
    /// easing then carries the view to wherever the ball came to rest.
    /// </summary>
    private void FollowCueBall(double delta)
    {
        if (Target == null || !IsInstanceValid(Target))
            return;

        if (PoolGame?.SimulationRunner != null && PoolGame.SimulationRunner.IsPlaying)
            return;

        var blend = 1.0f - Mathf.Exp(-FollowSharpness * (float)delta);
        GlobalPosition = GlobalPosition.Lerp(Target.GlobalPosition, blend);
    }

    private void ConnectSignals()
    {
        SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
        SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
        SignalUtil.ConnectGuarded(PoolGame.BallPlacementManager, BallPlacementManager.SignalName.PlacementFinished, new Callable(this, MethodName.OnPlacementFinished));
    }

    public override void _ExitTree()
    {
        if (PoolGame == null)
            return;

        SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
        SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
        SignalUtil.DisconnectGuarded(PoolGame.BallPlacementManager, BallPlacementManager.SignalName.PlacementFinished, new Callable(this, MethodName.OnPlacementFinished));
    }

    private void InitializePositions()
    {
        _rotY = Rotation.Y;
        if (ElevationNode != null)
        {
            _rotX = ElevationNode.Rotation.X;
            if (ElevationNode.GetChildCount() > 0)
            {
                var camChild = ElevationNode.GetChild<Node3D>(0);
                if (camChild != null)
                {
                    _cameraNode = camChild;
                    var pos = camChild.Position;
                    pos.Z = DistanceFromBall;
                    pos.Y = HeightOffset;
                    camChild.Position = pos;
                }
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsMultiplayerAuthority())
            return;

        var dynamicLimit = CalculateDynamicLimit();

        if (_rotX > dynamicLimit)
        {
            _rotX = Mathf.Lerp(_rotX, dynamicLimit, 0.1f);
            var elevRot = ElevationNode.Rotation;
            elevRot.X = _rotX;
            ElevationNode.Rotation = elevRot;
        }

        SyncPlayerModelRotation();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsMultiplayerAuthority())
            return;

        if (@event is InputEventMouseButton)
            InputFocus.Capture();
        else if (@event.IsActionPressed("ui_cancel"))
            InputFocus.Release();

        if (!InputFocus.IsCaptured)
            return;

        if (@event is InputEventMouseMotion motion)
            ApplyRotation(motion.Relative);
    }

    private void ApplyRotation(Vector2 relativeMotion)
    {
        _rotY -= relativeMotion.X * MouseSensitivity;
        var rot = Rotation;
        rot.Y = _rotY;
        Rotation = rot;

        if (ElevationNode == null)
            return;

        SyncPlayerModelRotation();

        var deltaMouse = relativeMotion.Y * MouseSensitivity;
        var currentLimit = CalculateDynamicLimit();

        if (_headAngle < -0.0001f && deltaMouse < 0)
        {
            _headAngle -= deltaMouse;

            if (_headAngle > 0)
            {
                var remainder = -_headAngle;
                _headAngle = 0.0f;
                _rotX += remainder;
            }
        }
        else
        {
            _rotX += deltaMouse;
        }

        if (_rotX > currentLimit)
        {
            var excess = _rotX - currentLimit;
            _rotX = currentLimit;
            _headAngle -= excess;
        }

        _headAngle = Mathf.Max(_headAngle, Mathf.DegToRad(MaxNeckLookUpDeg));
        _rotX = Mathf.Clamp(_rotX, Mathf.DegToRad(LimitCeilingDeg), currentLimit);

        var elevRot = ElevationNode.Rotation;
        elevRot.X = _rotX;
        ElevationNode.Rotation = elevRot;

        if (_cameraNode != null)
        {
            var camRot = _cameraNode.Rotation;
            camRot.X = -_headAngle;
            _cameraNode.Rotation = camRot;
        }
    }

    private float CalculateDynamicLimit()
    {
        if (Cue == null)
            return Mathf.DegToRad(LimitFloorDeg);

        // Reads the cue's TARGET pitch, not its live eased rotation: chasing a value that is
        // itself easing toward something made the camera limit and the cue drift after each other.
        var cueLimit = Cue.TargetElevationRad - Mathf.DegToRad(CueOffsetDeg);
        var floorLimit = Mathf.DegToRad(LimitFloorDeg);

        return Mathf.Min(cueLimit, floorLimit);
    }

    // Turn and placement transitions no longer need to reposition anything: FollowCueBall is
    // already riding the ball every frame, so the pivot is wherever the ball is by the time
    // control comes back.
    private void OnPlacementFinished()
    {
        SetPhysicsProcess(true);
    }

    private void OnTurnExtended(Godot.Collections.Dictionary context)
    {
    }

    private void OnTurnChanged(string nextPlayerName, Godot.Collections.Dictionary context)
    {
    }

    private void SyncPlayerModelRotation()
    {
        if (Player == null)
            return;

        var playerRot = Player.GlobalRotation;
        playerRot.Y = GlobalRotation.Y;
        Player.GlobalRotation = playerRot;

        var directionBack = GlobalTransform.Basis.Z.Normalized();
        var finalPos = GlobalPosition + (directionBack * 1.2f);

        finalPos.Y = PlayerFloorHeight;
        Player.GlobalPosition = finalPos;
    }
}
