using Godot;

/// <summary>Subtle four-part table rim: green turn, red occupied, grey empty.</summary>
public partial class PokerSeatPresenter : Node3D
{
    private const string TurnRingAnchorName = "TurnRingAnchor";
    private Node3D _turnRingRoot;

    private void RefreshTurnRing()
    {
        EnsureTurnRing();
        for (var seatIndex = 0; seatIndex < _turnRingSegments.Count; seatIndex++)
        {
            var playerId = _game.PlayerIdAtSeat(seatIndex);
            var occupied = !string.IsNullOrEmpty(playerId)
                && System.Array.IndexOf(_game.SeatOrder, playerId) >= 0;
            var color = !occupied || !_game.IsMatchActive
                ? EmptyTurnRingColor
                : _game.IsTurnOwner(playerId) ? ActiveTurnRingColor : OccupiedTurnRingColor;
            var material = _turnRingMaterials[seatIndex];
            material.AlbedoColor = color;
            material.Emission = color;
            _turnRingSegments[seatIndex].Visible = true;
        }
    }

    private void EnsureTurnRing()
    {
        if (_turnRingSegments.Count > 0 || Seats == null)
            return;

        _turnRingRoot = new Node3D
        {
            Name = "TurnRing",
            Transform = TurnRingFrame(),
        };
        AddChild(_turnRingRoot);

        var count = Mathf.Min(4, Seats.GetChildCount());
        for (var seatIndex = 0; seatIndex < count; seatIndex++)
        {
            if (Seats.GetChild(seatIndex) is not Node3D seat)
                continue;
            if (!TryTurnRingDirection(seat, _turnRingRoot.Transform, out var direction))
                continue;

            var material = BuildTurnRingMaterial();
            var segment = new MeshInstance3D
            {
                Name = $"TurnRingSeat{seatIndex}",
                Mesh = BuildTurnRingSegment(direction.Normalized(), material),
                Position = new Vector3(0.0f, TurnRingHeight, 0.0f),
            };
            _turnRingRoot.AddChild(segment);
            _turnRingMaterials.Add(material);
            _turnRingSegments.Add(segment);
        }
    }

    /// <summary>The editable root offset for the whole ring, in SeatPresenter space.</summary>
    private Transform3D TurnRingFrame()
    {
        var anchor = GetNodeOrNull<Node3D>(TurnRingAnchorName);
        if (anchor == null)
            return Transform3D.Identity;

        var local = GlobalTransform.AffineInverse() * anchor.GlobalTransform;
        return new Transform3D(local.Basis.Orthonormalized(), local.Origin);
    }

    /// <summary>
    /// Resolves the seat direction inside the authored ring frame. Translation moves the centre,
    /// while rotating the marker never detaches a coloured segment from its corresponding seat.
    /// </summary>
    private bool TryTurnRingDirection(
        Node3D seat, Transform3D ringFrame, out Vector2 direction)
    {
        direction = Vector2.Zero;
        if (seat == null)
            return false;

        var seatInPresenter = ToLocal(seat.GlobalPosition);
        var seatInRing = ringFrame.AffineInverse() * seatInPresenter;
        direction = new Vector2(seatInRing.X, seatInRing.Z);
        if (direction.LengthSquared() < 1e-6f)
            return false;

        direction = direction.Normalized();
        return true;
    }

    private StandardMaterial3D BuildTurnRingMaterial() => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = EmptyTurnRingColor,
        EmissionEnabled = true,
        Emission = EmptyTurnRingColor,
        EmissionEnergyMultiplier = 0.22f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private ImmediateMesh BuildTurnRingSegment(Vector2 direction, StandardMaterial3D material)
    {
        var radius = Mathf.Max(TurnRingRadius, 0.05f);
        var halfWidth = Mathf.Max(TurnRingWidth, 0.002f) * 0.5f;
        var steps = Mathf.Max(TurnRingArcSteps, 4);
        var centre = Mathf.Atan2(direction.Y, direction.X);
        var halfArc = Mathf.DegToRad(
            Mathf.Clamp(90.0f - TurnRingGapDegrees, 10.0f, 90.0f) * 0.5f);
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, material);
        for (var step = 0; step < steps; step++)
        {
            var angle0 = Mathf.Lerp(centre - halfArc, centre + halfArc, step / (float)steps);
            var angle1 = Mathf.Lerp(centre - halfArc, centre + halfArc, (step + 1) / (float)steps);
            var inner0 = RingPoint(angle0, radius - halfWidth);
            var outer0 = RingPoint(angle0, radius + halfWidth);
            var inner1 = RingPoint(angle1, radius - halfWidth);
            var outer1 = RingPoint(angle1, radius + halfWidth);
            AddRingTriangle(mesh, inner0, outer0, outer1);
            AddRingTriangle(mesh, inner0, outer1, inner1);
        }
        mesh.SurfaceEnd();
        return mesh;
    }

    private static Vector3 RingPoint(float angle, float radius) =>
        new(Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius);

    private static void AddRingTriangle(ImmediateMesh mesh, Vector3 a, Vector3 b, Vector3 c)
    {
        mesh.SurfaceSetNormal(Vector3.Up);
        mesh.SurfaceAddVertex(a);
        mesh.SurfaceAddVertex(b);
        mesh.SurfaceAddVertex(c);
    }
}
