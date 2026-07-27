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
    [Export] public float TransitionDuration = 0.25f;

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
    private Tween _tween;
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

    private void ConnectSignals()
    {
        SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
        SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
        SignalUtil.ConnectGuarded(PoolGame.BallPlacementManager, BallPlacementManager.SignalName.PlacementFinished, new Callable(this, MethodName.OnPlacementFinished));
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
            Input.MouseMode = Input.MouseModeEnum.Captured;
        else if (@event.IsActionPressed("ui_cancel"))
            Input.MouseMode = Input.MouseModeEnum.Visible;

        if (Input.MouseMode != Input.MouseModeEnum.Captured)
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

        var cueLimit = Cue.Rotation.X - Mathf.DegToRad(CueOffsetDeg);
        var floorLimit = Mathf.DegToRad(LimitFloorDeg);

        return Mathf.Min(cueLimit, floorLimit);
    }

    private async void OnPlacementFinished()
    {
        await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
        MoveSmoothlyToTarget();
        SetProcess(false);
        SetPhysicsProcess(true);
    }

    private void OnTurnExtended()
    {
        MoveSmoothlyToTarget();
    }

    private void OnTurnChanged(string nextPlayerName, Godot.Collections.Dictionary context)
    {
        MoveSmoothlyToTarget();
    }

    private void MoveSmoothlyToTarget()
    {
        if (Target == null)
            return;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.SetTrans(Tween.TransitionType.Cubic);
        _tween.SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(this, "global_position", Target.GlobalPosition, TransitionDuration);
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
