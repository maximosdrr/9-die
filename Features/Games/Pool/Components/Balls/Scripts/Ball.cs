using Godot;

[GlobalClass]
public partial class Ball : RigidBody3D
{
    private static readonly PackedScene WhiteBallMesh = GD.Load<PackedScene>("uid://duqktbfsd1n6u");
    private static readonly PackedScene[] ColoredBallMeshes =
    {
        GD.Load<PackedScene>("uid://dfbechio6ptbs"), GD.Load<PackedScene>("uid://xb3gwg7b6vym"),
        GD.Load<PackedScene>("uid://bgm0x1tow64ow"), GD.Load<PackedScene>("uid://bohcijga8l31y"),
        GD.Load<PackedScene>("uid://blo2j7vueioa8"), GD.Load<PackedScene>("uid://csl0h6mpj5wrf"),
        GD.Load<PackedScene>("uid://b3u1v561vo0k6"), GD.Load<PackedScene>("uid://dkmf4qijjxk0y"),
        GD.Load<PackedScene>("uid://cl3vhtcj52lj2"), GD.Load<PackedScene>("uid://c2pjjww311x5"),
        GD.Load<PackedScene>("uid://dsoxx0stji020"), GD.Load<PackedScene>("uid://bvnrqbdvnvih1"),
        GD.Load<PackedScene>("uid://bdr1hxm60mcdi"), GD.Load<PackedScene>("uid://3pq44lqvihfa"),
        GD.Load<PackedScene>("uid://buoe2fsbwjbk1"),
    };

    [Export] public BallResource Data;
    [Export] public int Index = 0;

    private int _textureId = 0;
    [Export]
    public int TextureId
    {
        get => _textureId;
        set
        {
            _textureId = value;
            if (IsInsideTree())
                CallDeferred(MethodName.UpdateVisual);
        }
    }

    public CollisionShape3D CollisionShape;
    public MultiplayerSynchronizer MultiplayerSynchronizerNode;

    [ExportGroup("Jump Physics")]
    [Export] public float JumpEfficiency = 1.2f;
    [Export] public float MinJumpAngle = 25.0f;
    [Export] public float CueMaxAngle = 65.0f;
    [Export] public float FloorTolerance = 0.02f;

    [ExportGroup("Movement Detection")]
    [Export] public float StopSpeedThreshold = 0.01f;
    [Export] public float StopCheckInterval = 0.1f;

    [Signal] public delegate void StoppedMovingEventHandler(Vector3 position);
    [Signal] public delegate void StartedMovingEventHandler();
    [Signal] public delegate void StrikedEventHandler();
    [Signal] public delegate void BallContactedEventHandler(Ball ball);
    [Signal] public delegate void JumpStartedEventHandler();
    [Signal] public delegate void JumpLandedEventHandler();
    [Signal] public delegate void TouchedRailEventHandler();

    public float Radius = 0.029f;
    public bool IsMoving = false;
    private Transform3D _initialTransform;
    private bool _isInAir = false;
    private float _stopCheckTimer = 0.0f;

    public override void _Ready()
    {
        CollisionShape = GetNode<CollisionShape3D>("CollisionShape3D");
        MultiplayerSynchronizerNode = GetNode<MultiplayerSynchronizer>("MultiplayerSynchronizer");

        UpdateVisual();

        if (Data != null)
        {
            Mass = Data.Mass;
            GravityScale = Data.GravityScale;
            LinearDamp = Data.LinearDamp;
            AngularDamp = Data.AngularDamp;

            ContinuousCd = Data.ContinuosCd;
            CanSleep = Data.CanSleep;

            var newMat = new PhysicsMaterial();
            newMat.Bounce = Data.Bounce;
            newMat.Friction = Data.Friction;
            newMat.Absorbent = Data.Absorbent;

            PhysicsMaterialOverride = newMat;
        }

        AddToGroup("Ball");

        if (CollisionShape != null && CollisionShape.Shape is SphereShape3D sphereShape)
            Radius = sphereShape.Radius;

        _initialTransform = GlobalTransform;
    }

    private void UpdateVisual()
    {
        foreach (var child in GetChildren())
        {
            if (child is MeshInstance3D || (child is Node3D && child.Name != "CollisionShape3D" && child is not MultiplayerSynchronizer _ && child is not StateMachine _))
            {
                if (child is VisualInstance3D)
                    child.QueueFree();
            }
        }

        Node newVisual = null;

        if (TextureId == 0)
            newVisual = WhiteBallMesh.Instantiate();
        else if (TextureId > 0 && (TextureId - 1) < ColoredBallMeshes.Length)
            newVisual = ColoredBallMeshes[TextureId - 1].Instantiate();

        if (newVisual != null)
            AddChild(newVisual);
    }

    public void Strike(Vector3 direction, float totalForce, Vector3 hitOffsetLocal = default)
    {
        EmitSignal(SignalName.Striked);

        if (totalForce <= 0.0f)
            return;

        var rawNormal = direction.Normalized();

        var attackAngleDeg = Mathf.RadToDeg(Mathf.Asin(Mathf.Abs(rawNormal.Y)));
        var isValidJump = rawNormal.Y < 0 && attackAngleDeg >= MinJumpAngle;

        Vector3 rawDir;
        var jumpFactor = 0.0f;

        if (isValidJump)
        {
            rawDir = rawNormal;
            var angleRange = CueMaxAngle - MinJumpAngle;
            var angleProgress = Mathf.Clamp(attackAngleDeg - MinJumpAngle, 0.0f, angleRange);

            jumpFactor = angleProgress / angleRange;
        }
        else
        {
            rawDir = new Vector3(direction.X, 0.0f, direction.Z).Normalized();
        }

        var offsetRatio = hitOffsetLocal.X / Radius;
        var maxAngleDeg = Data != null ? Data.MaxSquirtAngleDeg : 0.0f;
        var spinPower = Data != null ? Data.SpinPowerFactor : 1.0f;
        var maxAngleRad = Mathf.DegToRad(maxAngleDeg);
        var deflectionAngle = offsetRatio * maxAngleRad;

        var finalDir = rawDir.Rotated(Vector3.Up, deflectionAngle);
        var linearImpulse = finalDir * totalForce;

        if (isValidJump)
        {
            var verticalForce = Mathf.Abs(linearImpulse.Y) * JumpEfficiency * jumpFactor;
            linearImpulse.Y = verticalForce;
        }
        else
        {
            linearImpulse.Y = 0.0f;
        }

        var forward = -new Vector3(finalDir.X, 0.0f, finalDir.Z).Normalized();
        var up = Vector3.Up;
        var right = forward.Cross(up).Normalized();
        up = right.Cross(forward).Normalized();
        var aimBasis = new Basis(right, up, forward);

        var hitOffsetWorld = aimBasis * hitOffsetLocal;
        var rawTorque = hitOffsetWorld.Cross(linearImpulse);
        rawTorque.Y = -rawTorque.Y;

        var reducedTorque = rawTorque * spinPower;

        ApplyCentralImpulse(linearImpulse);
        ApplyTorqueImpulse(reducedTorque);
    }

    public void Respawn()
    {
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        GlobalTransform = _initialTransform;
        Sleeping = false;
        Visible = true;
        _isInAir = false;
        ProcessMode = ProcessModeEnum.Inherit;
    }

    public override void _PhysicsProcess(double delta)
    {
        var speed = LinearVelocity.Length();
        var slowThreshold = 0.3f;
        var stopThreshold = 0.05f;

        var minDamp = Data != null ? Data.AngularDamp : 1.0f;
        var maxDamp = 1.5f;

        var t = Mathf.InverseLerp(slowThreshold, stopThreshold, speed);
        t = Mathf.Clamp(t, 0.0f, 1.0f);
        AngularDamp = Mathf.Lerp(minDamp, maxDamp, t);

        if (_isInAir || speed > stopThreshold)
            CheckGroundState();

        UpdateMovementState(speed, delta);
    }

    private void UpdateMovementState(float speed, double delta)
    {
        _stopCheckTimer -= (float)delta;
        if (_stopCheckTimer > 0)
            return;
        _stopCheckTimer = StopCheckInterval;

        var isMovingNow = speed > StopSpeedThreshold;
        if (isMovingNow == IsMoving)
            return;

        IsMoving = isMovingNow;

        if (IsMoving)
            EmitSignal(SignalName.StartedMoving);
        else
            EmitSignal(SignalName.StoppedMoving, GlobalPosition);
    }

    private void CheckGroundState()
    {
        var spaceState = GetWorld3D().DirectSpaceState;

        var from = GlobalPosition;
        var to = from + Vector3.Down * (Radius + FloorTolerance);

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };

        var result = spaceState.IntersectRay(query);
        var isOnFloor = result.Count > 0;

        if (!isOnFloor && !_isInAir)
        {
            _isInAir = true;
            EmitSignal(SignalName.JumpStarted);
        }
        else if (isOnFloor && _isInAir)
        {
            _isInAir = false;
            EmitSignal(SignalName.JumpLanded);
        }
    }

    private void OnBodyEntered(Node body)
    {
        if (body is Ball ball)
            EmitSignal(SignalName.BallContacted, ball);
    }

    private void OnRigidBodyContactEntered(Node body)
    {
        if (body is Node3D node3D && node3D.IsInGroup("Cushion"))
            EmitSignal(SignalName.TouchedRail);
    }
}
