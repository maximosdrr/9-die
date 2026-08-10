using Godot;

/// <summary>Asset-independent motion curves shared by cards and chips.</summary>
public static class PokerMotion
{
    public static float Smooth(float t)
    {
        t = Mathf.Clamp(t, 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    public static float EaseOutCubic(float t)
    {
        t = 1.0f - Mathf.Clamp(t, 0.0f, 1.0f);
        return 1.0f - t * t * t;
    }

    /// <summary>
    /// A low, slightly curved throw. Horizontal travel decelerates into the felt, while vertical
    /// travel remains ballistic and returns exactly to the destination height.
    /// </summary>
    public static Vector3 CardThrow(Vector3 from, Vector3 to, float t, float arc, float sideways)
    {
        t = Mathf.Clamp(t, 0.0f, 1.0f);
        var arrival = EaseOutCubic(t);
        var position = from.Lerp(to, arrival);

        var flat = new Vector2(to.X - from.X, to.Z - from.Z);
        if (flat.LengthSquared() > 1e-6f)
        {
            var side = new Vector2(-flat.Y, flat.X).Normalized();
            var bend = Mathf.Sin(t * Mathf.Pi) * sideways;
            position.X += side.X * bend;
            position.Z += side.Y * bend;
        }

        position.Y += 4.0f * arc * t * (1.0f - t);
        return position;
    }

    /// <summary>A short chip toss/push. It never pauses in mid-air or carries a rigid stack.</summary>
    public static Vector3 ChipThrow(Vector2 from, Vector2 to, float t, float arc, float sideways)
    {
        t = Mathf.Clamp(t, 0.0f, 1.0f);
        var travel = Smooth(t);
        var flat = from.Lerp(to, travel);
        var direction = to - from;
        var side = direction.LengthSquared() > 1e-6f
            ? new Vector2(-direction.Y, direction.X).Normalized()
            : Vector2.Zero;

        return new Vector3(
            flat.X + side.X * Mathf.Sin(t * Mathf.Pi) * sideways,
            4.0f * arc * t * (1.0f - t),
            flat.Y + side.Y * Mathf.Sin(t * Mathf.Pi) * sideways);
    }

    /// <summary>Three-dimensional chip travel for payouts that finish on top of a player's stack.</summary>
    public static Vector3 ChipThrow(Vector3 from, Vector3 to, float t, float arc, float sideways)
    {
        t = Mathf.Clamp(t, 0.0f, 1.0f);
        var position = from.Lerp(to, Smooth(t));
        var flat = new Vector2(to.X - from.X, to.Z - from.Z);
        if (flat.LengthSquared() > 1e-6f)
        {
            var side = new Vector2(-flat.Y, flat.X).Normalized();
            var bend = Mathf.Sin(t * Mathf.Pi) * sideways;
            position.X += side.X * bend;
            position.Z += side.Y * bend;
        }
        position.Y += 4.0f * arc * t * (1.0f - t);
        return position;
    }
}
