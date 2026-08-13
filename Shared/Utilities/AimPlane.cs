using Godot;

/// <summary>
/// Where a seated player is pointing on a table surface.
///
/// A ray against the surface PLANE rather than a physics cast: the things laid out on a table are
/// rendered, not simulated, so they have no colliders, and the table's own collider is a fat
/// cylinder that says nothing about where on the cloth the ray landed. A mathematical plane through
/// the surface node is both cheaper and exactly the surface the layout is computed on.
/// </summary>
public static class AimPlane
{
    /// <summary>
    /// Where the screen centre meets <paramref name="surface"/>, in that node's local X/Z.
    ///
    /// The centre rather than the cursor because these modes capture the mouse — the crosshair is
    /// the cursor. Returns false when there is no camera, no surface, or the ray runs parallel to
    /// the cloth.
    /// </summary>
    public static bool TryAim(Camera3D camera, Node3D surface, out Vector2 surfaceLocal) =>
        TryAimAt(camera, surface, ScreenCentre(camera), out surfaceLocal);

    /// <summary>Same, for an explicit screen point — a real cursor, or a test driving the aim.</summary>
    public static bool TryAimAt(Camera3D camera, Node3D surface, Vector2 screenPoint, out Vector2 surfaceLocal)
    {
        surfaceLocal = Vector2.Zero;

        if (surface == null || !surface.IsInsideTree()
            || !TryScreenRay(camera, screenPoint, out var rayOrigin, out var rayDirection))
            return false;

        var plane = new Plane(surface.GlobalBasis.Y.Normalized(), surface.GlobalPosition);

        var hit = plane.IntersectsRay(rayOrigin, rayDirection);

        if (hit == null)
            return false;

        var local = surface.ToLocal(hit.Value);
        surfaceLocal = new Vector2(local.X, local.Z);
        return true;
    }

    /// <summary>The world-space ray passing through the exact centre of the camera image.</summary>
    public static bool TryCentreRay(
        Camera3D camera, out Vector3 rayOrigin, out Vector3 rayDirection) =>
        TryScreenRay(camera, ScreenCentre(camera), out rayOrigin, out rayDirection);

    public static bool TryScreenRay(
        Camera3D camera, Vector2 screenPoint, out Vector3 rayOrigin, out Vector3 rayDirection)
    {
        rayOrigin = Vector3.Zero;
        rayDirection = Vector3.Zero;
        if (camera == null || !camera.IsInsideTree())
            return false;

        rayOrigin = camera.ProjectRayOrigin(screenPoint);
        rayDirection = camera.ProjectRayNormal(screenPoint).Normalized();
        return rayOrigin.IsFinite() && rayDirection.IsFinite()
               && rayDirection.LengthSquared() > 0.999f;
    }

    /// <summary>
    /// Centre of the rectangle actually rendered by this camera. Keeping the rectangle origin is
    /// important for embedded/sub-viewports: half of the size alone is only correct when it starts
    /// at (0, 0).
    /// </summary>
    public static Vector2 ScreenCentre(Camera3D camera)
    {
        if (camera == null || !camera.IsInsideTree())
            return Vector2.Zero;

        var visible = camera.GetViewport().GetVisibleRect();
        return visible.Position + visible.Size * 0.5f;
    }
}
