using Godot;

/// <summary>
/// Puts the seat markers on the real chairs.
///
/// Derived from the chair nodes at load rather than typed into the scene, so moving a chair in the
/// editor moves where the player sits — no pair of transforms to keep in sync by hand, and no
/// chance of the two drifting apart. Also gives each chair a collider, since the bar's furniture
/// is imported as bare meshes.
///
/// Body and head are placed separately on purpose. A chair sits about a metre from the middle of
/// the table; putting the camera there too would push whatever is played on the cloth far enough
/// away to undo the work that made it readable. A seated player leans in, so the eye is offset
/// toward the table while the body stays on the chair, where the sitting animation will need it.
/// </summary>
[GlobalClass]
public partial class TableSeatAnchors : Node3D
{
    /// <summary>In seating order. Each one claims the seat marker at the same index.</summary>
    [Export] public Godot.Collections.Array<Node3D> Chairs = new();

    [ExportGroup("Body placement")]
    /// <summary>
    /// Fine adjustment of the character root in the chair's local axes. It stays at floor height
    /// while the temporary standing idle is in use; the future seated animation can be aligned by
    /// editing this one value in the inspector.
    /// </summary>
    [Export] public Vector3 BodyOffset = Vector3.Zero;

    /// <summary>Eye height above the character root while seated.</summary>
    [Export] public float EyeHeight = 1.13f;

    /// <summary>How far the player leans in over the table from the chair.</summary>
    [Export] public float EyeLean = 0.30f;

    /// <summary>
    /// Used when a seat has no hand-placed StandExit child. Positive local Z is behind the chair,
    /// away from the table, so the walking collider is never restored inside the furniture.
    /// </summary>
    [Export] public float StandBackDistance = 0.55f;

    [ExportGroup("Chair collision")]
    [Export] public bool BuildChairColliders = true;

    /// <summary>
    /// Collider height as a fraction of the chair's full height. A box up to the backrest would
    /// stop a player walking past a chair that is mostly empty air above the seat.
    /// </summary>
    [Export] public float ColliderHeightFactor = 0.55f;

    /// <summary>Pulled in a little so a player can stand right beside a chair without snagging.</summary>
    [Export] public float ColliderInset = 0.06f;

    public override void _Ready()
    {
        AlignSeatsToChairs();

        if (BuildChairColliders)
            GiveChairsColliders();
    }

    private void AlignSeatsToChairs()
    {
        var seat = 0;

        foreach (var child in GetChildren())
        {
            if (child is not Marker3D marker)
                continue;

            if (seat >= Chairs.Count || Chairs[seat] == null)
            {
                seat++;
                continue;
            }

            PlaceSeat(marker, Chairs[seat]);
            seat++;
        }
    }

    private void PlaceSeat(Marker3D marker, Node3D chair)
    {
        var chairTransform = chair.GlobalTransform;

        // The chair supplies the body position, while the table centre supplies the exact facing.
        // Imported chair rotations are intentionally only approximate; copying one here can leave
        // the seated camera several degrees away from the play area after an artist moves a chair.
        var toTable = (GlobalPosition - chairTransform.Origin) with { Y = 0.0f };
        if (toTable.LengthSquared() < 1e-6f)
            return;

        toTable = toTable.Normalized();

        var yaw = Mathf.Atan2(-toTable.X, -toTable.Z);
        var seatPosition = chairTransform.Origin
            + chairTransform.Basis.Orthonormalized() * BodyOffset;

        marker.GlobalTransform = new Transform3D(
            Basis.FromEuler(new Vector3(0.0f, yaw, 0.0f)), seatPosition);

        // The eye rides forward of the body — the lean — and above it.
        var eye = marker.GetNodeOrNull<Node3D>("SeatView");
        if (eye != null)
            eye.Position = new Vector3(0.0f, EyeHeight, -EyeLean);

        // Kept as a child marker so an artist can place the exact get-up point per chair later. The
        // generated default is already outside the chair collider and remains editable in the scene.
        var standExit = marker.GetNodeOrNull<Marker3D>("StandExit");
        if (standExit != null && standExit.Position.IsZeroApprox())
            standExit.Position = new Vector3(0.0f, 0.0f, StandBackDistance);
    }

    /// <summary>
    /// Wraps each chair in a body the player can bump into. Built from the mesh's own bounds so it
    /// tracks whatever model is in the scene, and only up to seat height plus a bit — a box as tall
    /// as the backrest would be mostly empty air the player could not walk through.
    /// </summary>
    private void GiveChairsColliders()
    {
        foreach (var chair in Chairs)
        {
            if (chair is not MeshInstance3D mesh || mesh.Mesh == null)
                continue;

            if (mesh.GetNodeOrNull<StaticBody3D>("Collision") != null)
                continue;

            var bounds = mesh.GetAabb();
            var size = bounds.Size;

            var box = new BoxShape3D
            {
                Size = new Vector3(
                    Mathf.Max(size.X - ColliderInset, 0.05f),
                    Mathf.Max(size.Y * ColliderHeightFactor, 0.05f),
                    Mathf.Max(size.Z - ColliderInset, 0.05f)),
            };

            // Layer 3 (Object) is what the rest of the bar's furniture blocks the player with.
            var body = new StaticBody3D { Name = "Collision", CollisionLayer = 4, CollisionMask = 0 };
            var shape = new CollisionShape3D { Name = "CollisionShape3D", Shape = box };

            body.AddChild(shape);
            mesh.AddChild(body);

            shape.Position = bounds.Position + new Vector3(
                size.X * 0.5f, size.Y * ColliderHeightFactor * 0.5f, size.Z * 0.5f);
        }
    }
}
