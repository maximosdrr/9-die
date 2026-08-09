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

		if (camera == null || surface == null || !camera.IsInsideTree() || !surface.IsInsideTree())
			return false;

		var plane = new Plane(surface.GlobalBasis.Y.Normalized(), surface.GlobalPosition);

		var hit = plane.IntersectsRay(
			camera.ProjectRayOrigin(screenPoint), camera.ProjectRayNormal(screenPoint));

		if (hit == null)
			return false;

		var local = surface.ToLocal(hit.Value);
		surfaceLocal = new Vector2(local.X, local.Z);
		return true;
	}

	private static Vector2 ScreenCentre(Camera3D camera) =>
		camera != null && camera.IsInsideTree()
			? camera.GetViewport().GetVisibleRect().Size * 0.5f
			: Vector2.Zero;
}
