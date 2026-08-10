using System;

namespace Pool.Simulation;

/// <summary>
/// Everything a shot is. Deliberately tiny and unit-normalised: this is the only thing that
/// travels over the network (roughly 20 bytes), replacing the per-ball position/velocity stream
/// the old implementation shipped every frame. It is also trivially validatable server-side,
/// unlike a raw impulse vector — the old RequestStrike accepted any direction, which let a
/// modified client fire a vertical jump shot past the elevation limit.
/// </summary>
public readonly struct ShotInput
{
    /// <summary>Aim direction on the table, radians. 0 points along +Z.</summary>
    public readonly double AimYaw;

    /// <summary>Downward tilt of the cue, radians. 0 is level; positive dips the butt up for a jump.</summary>
    public readonly double Elevation;

    /// <summary>
    /// Cue-stick speed at impact, m/s. The resulting ball speed is calculated from the cue mass,
    /// restitution and tip position by <see cref="CueStrikeModel"/>.
    /// </summary>
    public readonly double Speed;

    /// <summary>Horizontal tip offset as a fraction of the ball radius. Negative is left english.</summary>
    public readonly double TipOffsetX;

    /// <summary>Vertical tip offset as a fraction of the ball radius. Negative is draw, positive is follow.</summary>
    public readonly double TipOffsetY;

    /// <summary>
    /// Beyond about half a radius the tip slides off the ball in reality — a miscue. Clamping
    /// here means no input path, local or remote, can produce spin that could not be struck.
    /// </summary>
    public const double MaxTipOffset = 0.5;

    public const double MaxSpeed = 9.0;

    public ShotInput(double aimYaw, double elevation, double speed, double tipOffsetX, double tipOffsetY)
    {
        AimYaw = aimYaw;
        Elevation = elevation;
        Speed = speed;
        TipOffsetX = tipOffsetX;
        TipOffsetY = tipOffsetY;
    }

    /// <summary>
    /// Returns a copy with every field forced into its legal range. The server runs this on
    /// anything it receives, so an out-of-range value becomes a legal shot rather than a rejected
    /// one or an exploit.
    /// </summary>
    public ShotInput Sanitized()
    {
        var yaw = double.IsFinite(AimYaw) ? Math.IEEERemainder(AimYaw, Math.PI * 2.0) : 0.0;
        var speed = double.IsFinite(Speed) ? Math.Clamp(Speed, 0.0, MaxSpeed) : 0.0;
        var elevation = double.IsFinite(Elevation) ? Math.Clamp(Elevation, 0.0, Math.PI / 3.0) : 0.0;

        var offsetX = double.IsFinite(TipOffsetX) ? TipOffsetX : 0.0;
        var offsetY = double.IsFinite(TipOffsetY) ? TipOffsetY : 0.0;
        var offsetLength = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
        if (offsetLength > MaxTipOffset)
        {
            var scale = MaxTipOffset / offsetLength;
            offsetX *= scale;
            offsetY *= scale;
        }

        return new ShotInput(yaw, elevation, speed, offsetX, offsetY);
    }
}
