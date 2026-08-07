using Godot;
using Pool.Simulation;

/// <summary>
/// A ball as the scene sees it: visuals, identity and the signals the rest of the game listens
/// to. It is NOT a physics body — PoolSimulationRunner computes the whole shot analytically and
/// drives this node's transform from the result.
///
/// The signals below are still emitted, but by the runner off the simulated event timeline
/// rather than by contact callbacks, so they can no longer be missed (max_contacts_reported used
/// to silently swallow rail hits during a break) or fire late.
/// </summary>
[GlobalClass]
public partial class Ball : Node3D
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

    public MultiplayerSynchronizer MultiplayerSynchronizerNode;

    [Signal] public delegate void BallContactedEventHandler(Ball ball);
    [Signal] public delegate void TouchedRailEventHandler();
    [Signal] public delegate void BouncedOnClothEventHandler();

    /// <summary>Ball radius in metres. Single source of truth is the simulation's regulation value.</summary>
    public float Radius => (float)BilliardConstants.Radius;

    /// <summary>False once potted or driven off — the runner stops drawing and simulating it.</summary>
    public bool InPlay { get; private set; } = true;

    /// <summary>
    /// While ball-in-hand is being previewed, the real gameplay ball must stay hidden even if a
    /// late authoritative snapshot marks it in play again.
    /// </summary>
    public bool PlacementPreviewActive { get; private set; }

    /// <summary>
    /// Last known velocity, published by the runner purely so the impact sounds can scale
    /// themselves. Nothing reads it to make gameplay decisions.
    /// </summary>
    public Vector3 LinearVelocity { get; private set; }

    /// <summary>Last spin applied, so a potted ball can keep tumbling as it drops.</summary>
    public Vector3 AngularVelocitySnapshot { get; private set; }

    public override void _Ready()
    {
        MultiplayerSynchronizerNode = GetNodeOrNull<MultiplayerSynchronizer>("MultiplayerSynchronizer");
        UpdateVisual();
        AddToGroup("Ball");
    }

    private Node _visual;

    /// <summary>
    /// Creates a presentation-only copy of this ball for ball-in-hand previews. It deliberately
    /// has no Ball script, synchronizer, sounds or gameplay identity: moving it can never alter
    /// the authoritative table state.
    /// </summary>
    public Node3D CreatePlacementGhost()
    {
        var ghost = new Node3D { Name = $"PlacementGhost{Index}" };
        var scene = TextureId == 0
            ? WhiteBallMesh
            : TextureId > 0 && TextureId - 1 < ColoredBallMeshes.Length
                ? ColoredBallMeshes[TextureId - 1]
                : null;

        if (scene == null)
            return ghost;

        var visual = scene.Instantiate<Node3D>();
        ghost.AddChild(visual);
        MakeGhostTranslucent(visual);
        return ghost;
    }

    private static void MakeGhostTranslucent(Node node)
    {
        if (node is GeometryInstance3D geometry)
        {
            geometry.Transparency = 0.45f;
            geometry.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        foreach (var child in node.GetChildren())
            MakeGhostTranslucent(child);
    }

    private void UpdateVisual()
    {
        // Tracking the node we spawned, rather than sweeping every VisualInstance3D child, keeps
        // this from also deleting anything else parented to the ball (the DebugLabel, for one).
        if (IsInstanceValid(_visual))
            _visual.QueueFree();

        _visual = null;

        if (TextureId == 0)
            _visual = WhiteBallMesh.Instantiate();
        else if (TextureId > 0 && (TextureId - 1) < ColoredBallMeshes.Length)
            _visual = ColoredBallMeshes[TextureId - 1].Instantiate();

        if (_visual != null)
            AddChild(_visual);
    }

    /// <summary>
    /// Publishes this frame's velocity (read only by the impact sounds) and rolls the visual mesh
    /// to match the simulated spin, which is purely cosmetic.
    /// </summary>
    public void ApplyMotion(Vector3 linearVelocity, Vector3 angularVelocity, float delta)
    {
        LinearVelocity = linearVelocity;
        AngularVelocitySnapshot = angularVelocity;

        var spinRate = angularVelocity.Length();
        if (spinRate < 1e-4f)
            return;

        Rotate(angularVelocity / spinRate, spinRate * delta);
    }

    /// <summary>
    /// Marks the ball in or out of play. Note this does NOT hide it: a potted ball still has to
    /// be seen falling into the pocket, so visibility is owned by the drop animation and only
    /// forced back on when the ball returns to play.
    /// </summary>
    public void SetInPlay(bool inPlay)
    {
        InPlay = inPlay;
        if (inPlay && !PlacementPreviewActive)
            Visible = true;
    }

    public void SetPlacementPreviewActive(bool active)
    {
        PlacementPreviewActive = active;
        if (active)
            Visible = false;
    }

    public void NotifyBallContacted(Ball other) => EmitSignal(SignalName.BallContacted, other);

    public void NotifyTouchedRail() => EmitSignal(SignalName.TouchedRail);

    public void NotifyBouncedOnCloth() => EmitSignal(SignalName.BouncedOnCloth);
}
