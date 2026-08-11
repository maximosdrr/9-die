using Godot;

/// <summary>
/// Normalizes captured-pointer motion across viewport stretch modes. ScreenRelative is stable in
/// screen pixels; Relative is kept as a fallback for platforms that do not populate it.
/// </summary>
public static class PointerMotion
{
    public static Vector2 ReadScreenDelta(InputEventMouseMotion motion)
    {
        if (motion == null)
            return Vector2.Zero;

        var delta = motion.ScreenRelative.IsZeroApprox()
            ? motion.Relative
            : motion.ScreenRelative;

        return float.IsFinite(delta.X) && float.IsFinite(delta.Y)
            ? delta
            : Vector2.Zero;
    }

    public static Vector2 ReadScreenVelocity(InputEventMouseMotion motion)
    {
        if (motion == null)
            return Vector2.Zero;

        var velocity = motion.ScreenVelocity.IsZeroApprox()
            ? motion.Velocity
            : motion.ScreenVelocity;

        return float.IsFinite(velocity.X) && float.IsFinite(velocity.Y)
            ? velocity
            : Vector2.Zero;
    }
}
