using Godot;
using System.Collections.Generic;
using Pool.Simulation;

/// <summary>
/// The editable definition of a table's ball physics. Everything is read from marker nodes you
/// place in the table's own scene, so each table carries its own shape and adjusting a marker in
/// the viewport moves the thing balls actually interact with.
///
/// Markers are ordinary CollisionShape3D nodes under Area3D parents that monitor nothing. That
/// buys the full Godot resize/move gizmo in the viewport — the same handles used to build the
/// table in the first place — without any of them taking part in physics.
///
///   Cloth   — one box. Its footprint is the playing surface: where balls may come to rest, and
///             the boundary past which a ball counts as driven off the table.
///   Rails   — one box per cushion run. The face pointing toward the table is the line balls
///             bounce off; the box's length is the run's length. Nothing is mirrored or inferred,
///             so a table with an unusual rail layout is described by placing the boxes it has.
///   Pockets — one cylinder (or sphere/capsule) per pocket. Centre and radius are the capture circle.
///
/// This node's origin is the simulation origin, and ball positions are expressed relative to it.
/// Markers are its children, so moving this node moves the whole table together.
/// </summary>
[Tool]
[GlobalClass]
public partial class PoolTableGeometry : Node3D
{
    [ExportGroup("Marker nodes")]
    /// <summary>CollisionShape3D with a BoxShape3D covering the playing surface.</summary>
    [Export] public CollisionShape3D ClothMarker;

    /// <summary>Parent whose CollisionShape3D children (BoxShape3D) each describe one cushion run.</summary>
    [Export] public Node3D RailMarkers;

    /// <summary>Parent whose CollisionShape3D children each describe one pocket.</summary>
    [Export] public Node3D PocketMarkers;

    [ExportGroup("Tuning")]
    /// <summary>Pulls every pocket's capture circle inward (positive) without moving the markers.</summary>
    [Export] public float PocketRadiusAdjust = 0.0f;

    /// <summary>Pushes every cushion line inward (positive) without moving the markers.</summary>
    [Export] public float CushionInset = 0.0f;

    [ExportGroup("Editor gizmo")]
    [Export] public bool ShowGizmo = true;
    [Export] public Color CushionColor = new(0.2f, 1.0f, 0.4f);
    [Export] public Color PocketColor = new(1.0f, 0.35f, 0.2f);
    [Export] public Color ClothColor = new(0.3f, 0.6f, 1.0f);

    private MeshInstance3D _gizmo;
    private string _lastGizmoKey = "";

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint())
            return;

        // No signal fires when a marker is dragged, so the gizmo polls a cheap description of
        // every source and rebuilds only when something actually moved.
        var key = DescribeSources();
        if (key == _lastGizmoKey)
            return;

        _lastGizmoKey = key;
        RefreshGizmo();
    }

    /// <summary>
    /// Reads the markers and produces the spec the simulation runs on. A table with no markers
    /// yet falls back to a symmetric regulation table rather than throwing, so a half-built
    /// scene still loads.
    /// </summary>
    public TableSpec BuildSpec()
    {
        var (centre, halfWidth, halfLength) = ReadCloth();
        var pockets = ReadPockets();
        var cushions = ReadRails();

        if (cushions.Count == 0 || pockets.Count == 0)
        {
            GD.PushWarning(
                $"[{Name}] Marcadores incompletos (tabelas: {cushions.Count}, caçapas: {pockets.Count}); " +
                "usando mesa padrão simétrica.");
            return TableSpec.CreateDefault(halfWidth, halfLength);
        }

        return new TableSpec(centre, halfWidth, halfLength, cushions, pockets);
    }

    private (Vec3d Centre, double HalfWidth, double HalfLength) ReadCloth()
    {
        if (ClothMarker == null || ClothMarker.Shape is not BoxShape3D box || !IsInsideTree())
            return (Vec3d.Zero, 0.5088, 1.0492);

        var local = ToLocal(ClothMarker.GlobalPosition);
        var scale = ClothMarker.GlobalBasis.Scale;

        return (
            new Vec3d(local.X, 0.0, local.Z),
            Mathf.Abs(box.Size.X * scale.X) * 0.5,
            Mathf.Abs(box.Size.Z * scale.Z) * 0.5);
    }

    private List<Pocket> ReadPockets()
    {
        var pockets = new List<Pocket>();
        if (PocketMarkers == null || !IsInsideTree())
            return pockets;

        foreach (var child in PocketMarkers.GetChildren())
        {
            if (child is not CollisionShape3D shape || shape.Shape == null)
                continue;

            var radius = shape.Shape switch
            {
                CylinderShape3D cylinder => cylinder.Radius,
                CapsuleShape3D capsule => capsule.Radius,
                SphereShape3D sphere => sphere.Radius,
                BoxShape3D box => Mathf.Min(box.Size.X, box.Size.Z) * 0.5f,
                _ => 0.0f,
            };

            if (radius <= 0.0f)
                continue;

            // Only the footprint matters: a pocket is a circle on the cloth however deep the
            // marker was sunk.
            var local = ToLocal(shape.GlobalPosition);
            pockets.Add(new Pocket(
                new Vec3d(local.X, 0.0, local.Z),
                Mathf.Max(0.001f, radius - PocketRadiusAdjust)));
        }

        return pockets;
    }

    private List<CushionSegment> ReadRails()
    {
        var cushions = new List<CushionSegment>();
        if (RailMarkers == null || !IsInsideTree())
            return cushions;

        foreach (var child in RailMarkers.GetChildren())
        {
            if (child is CollisionShape3D shape && TryReadRail(shape, out var segment))
                cushions.Add(segment);
        }

        return cushions;
    }

    /// <summary>
    /// Turns a rail box into the line balls bounce off: of the box's four vertical faces, the one
    /// nearest the middle of the table is the playing face, and the cushion runs along it. Reading
    /// the face rather than the box centre means the rail's THICKNESS is what you adjust to move
    /// the playing line, which is how a real rail behaves.
    /// </summary>
    private bool TryReadRail(CollisionShape3D shape, out CushionSegment segment)
    {
        segment = default;

        if (shape.Shape is not BoxShape3D box)
            return false;

        var toLocal = GlobalTransform.AffineInverse() * shape.GlobalTransform;
        var half = box.Size * 0.5f;

        var axes = new[]
        {
            (Vector: toLocal.Basis.X, Extent: half.X),
            (Vector: toLocal.Basis.Z, Extent: half.Z),
        };

        // The playing face is on the rail's THIN axis. Picking simply "the face nearest the middle
        // of the table" looks right but isn't: on a long side rail, the END CAP near a side pocket
        // is closer to the centre than the playing face is, and the cushion would come out as a
        // stub across the rail's tip.
        var thinAxis = axes[0].Extent <= axes[1].Extent ? 0 : 1;
        var longAxis = 1 - thinAxis;

        if (axes[thinAxis].Vector.LengthSquared() <= 0.0f || axes[longAxis].Vector.LengthSquared() <= 0.0f)
            return false;

        var centre = new Vector2((float)PlayCentre().X, (float)PlayCentre().Z);
        var here = new Vector2(toLocal.Origin.X, toLocal.Origin.Z);

        // Of the two long faces, the playing one is whichever faces the table.
        var offset = axes[thinAxis].Vector * axes[thinAxis].Extent;
        var positiveFace = new Vector2(here.X + offset.X, here.Y + offset.Z);
        var sign = positiveFace.DistanceTo(centre) < here.DistanceTo(centre) ? 1.0f : -1.0f;

        // This points from the rail's middle toward the table, so it is already the inward normal.
        var inward = (axes[thinAxis].Vector * sign);
        var normal = new Vector3(inward.X, 0.0f, inward.Z).Normalized();
        if (normal.LengthSquared() <= 0.0f)
            return false;

        var face = toLocal.Origin + inward * axes[thinAxis].Extent + normal * CushionInset;
        var along = axes[longAxis].Vector * axes[longAxis].Extent;

        var start = new Vector3(face.X - along.X, 0.0f, face.Z - along.Z);
        var end = new Vector3(face.X + along.X, 0.0f, face.Z + along.Z);

        segment = new CushionSegment(
            new Vec3d(start.X, 0.0, start.Z),
            new Vec3d(end.X, 0.0, end.Z),
            new Vec3d(normal.X, 0.0, normal.Z));

        return true;
    }

    private Vec3d PlayCentre()
    {
        var (centre, _, _) = ReadCloth();
        return centre;
    }

    private string DescribeSources()
    {
        if (!ShowGizmo)
            return "off";

        var key = $"{PocketRadiusAdjust}|{CushionInset}|{CushionColor}|{PocketColor}|{ClothColor}";
        key += Describe(ClothMarker);

        foreach (var group in new[] { RailMarkers, PocketMarkers })
        {
            if (group == null)
                continue;

            foreach (var child in group.GetChildren())
            {
                if (child is CollisionShape3D shape)
                    key += Describe(shape);
            }
        }

        return key;
    }

    private static string Describe(CollisionShape3D shape)
    {
        if (shape == null)
            return "|-";

        var size = shape.Shape switch
        {
            BoxShape3D box => box.Size.ToString(),
            CylinderShape3D cylinder => cylinder.Radius.ToString(),
            CapsuleShape3D capsule => capsule.Radius.ToString(),
            SphereShape3D sphere => sphere.Radius.ToString(),
            _ => "?",
        };

        return $"|{shape.Transform}{size}";
    }

    private void RefreshGizmo()
    {
        EnsureGizmoNode();
        if (_gizmo == null)
            return;

        _gizmo.Visible = ShowGizmo;
        if (!ShowGizmo)
            return;

        var spec = BuildSpec();
        var mesh = new ImmediateMesh();

        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
        };

        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);

        AddRectangle(mesh, spec, ClothColor);

        foreach (var cushion in spec.Cushions)
        {
            var start = ToVector3(cushion.Start);
            var end = ToVector3(cushion.End);

            AddLine(mesh, start, end, CushionColor);

            // Short inward tick at the midpoint showing which way the cushion faces.
            var mid = (start + end) * 0.5f;
            AddLine(mesh, mid, mid + ToVector3(cushion.Normal) * 0.05f, CushionColor);
        }

        foreach (var pocket in spec.Pockets)
            AddCircle(mesh, ToVector3(pocket.Center), (float)pocket.Radius, PocketColor);

        mesh.SurfaceEnd();
        _gizmo.Mesh = mesh;
    }

    private static void AddRectangle(ImmediateMesh mesh, TableSpec spec, Color color)
    {
        var centre = ToVector3(spec.PlayCentre);
        var x = (float)spec.HalfWidth;
        var z = (float)spec.HalfLength;

        var a = centre + new Vector3(-x, 0.0f, -z);
        var b = centre + new Vector3(x, 0.0f, -z);
        var c = centre + new Vector3(x, 0.0f, z);
        var d = centre + new Vector3(-x, 0.0f, z);

        AddLine(mesh, a, b, color);
        AddLine(mesh, b, c, color);
        AddLine(mesh, c, d, color);
        AddLine(mesh, d, a, color);
    }

    private static void AddLine(ImmediateMesh mesh, Vector3 from, Vector3 to, Color color)
    {
        mesh.SurfaceSetColor(color);
        mesh.SurfaceAddVertex(from);
        mesh.SurfaceSetColor(color);
        mesh.SurfaceAddVertex(to);
    }

    private static void AddCircle(ImmediateMesh mesh, Vector3 centre, float radius, Color color)
    {
        const int steps = 32;

        for (var i = 0; i < steps; i++)
        {
            var a = Mathf.Tau * i / steps;
            var b = Mathf.Tau * (i + 1) / steps;

            AddLine(mesh,
                centre + new Vector3(Mathf.Cos(a), 0.0f, Mathf.Sin(a)) * radius,
                centre + new Vector3(Mathf.Cos(b), 0.0f, Mathf.Sin(b)) * radius,
                color);
        }
    }

    private void EnsureGizmoNode()
    {
        if (_gizmo != null && IsInstanceValid(_gizmo))
            return;

        _gizmo = GetNodeOrNull<MeshInstance3D>("PhysicsGizmo");
        if (_gizmo != null)
            return;

        // Deliberately left without an owner so it is never written into the .tscn.
        _gizmo = new MeshInstance3D { Name = "PhysicsGizmo" };
        AddChild(_gizmo);
    }

    private static Vector3 ToVector3(Vec3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
