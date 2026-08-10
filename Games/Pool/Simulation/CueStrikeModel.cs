using System;

namespace Pool.Simulation;

/// <summary>
/// Result of the short cue-tip/ball impact. The shot input describes the cue before contact;
/// this result describes the velocity imparted to the ball after one effective contact impulse.
/// </summary>
public readonly struct CueStrikeResult
{
    public readonly Vec3d LinearVelocity;
    public readonly Vec3d AngularVelocity;
    public readonly double Impulse;

    public CueStrikeResult(Vec3d linearVelocity, Vec3d angularVelocity, double impulse)
    {
        LinearVelocity = linearVelocity;
        AngularVelocity = angularVelocity;
        Impulse = impulse;
    }

    public double BallKineticEnergy =>
        0.5 * BilliardConstants.Mass * LinearVelocity.LengthSquared
        + 0.5 * BilliardConstants.MomentOfInertia * AngularVelocity.LengthSquared;
}

/// <summary>
/// Deterministic rigid-impact approximation for the cue and cue ball.
///
/// The requested speed belongs to the cue stick. Cue mass, tip restitution and the rotational
/// effective mass of an off-centre contact determine the impulse, so spin consumes some of the
/// same finite energy that would otherwise become linear ball speed.
/// </summary>
public static class CueStrikeModel
{
    public static CueStrikeResult Calculate(ShotInput rawShot)
    {
        var shot = rawShot.Sanitized();
        if (shot.Speed <= 0.0)
            return new CueStrikeResult(Vec3d.Zero, Vec3d.Zero, 0.0);

        var horizontal = new Vec3d(Math.Sin(shot.AimYaw), 0.0, Math.Cos(shot.AimYaw));
        var direction = (horizontal * Math.Cos(shot.Elevation)
                         - Vec3d.Up * Math.Sin(shot.Elevation)).Normalized();

        var right = direction.Cross(Vec3d.Up).Normalized();
        if (right.LengthSquared <= 0.0)
            right = new Vec3d(1.0, 0.0, 0.0);
        var up = right.Cross(direction).Normalized();

        var offsetX = shot.TipOffsetX * BilliardConstants.Radius;
        var offsetY = shot.TipOffsetY * BilliardConstants.Radius;
        var tangentOffset = right * offsetX + up * offsetY;

        // Side english deflects the ball away from the side of the tip. The exact value varies by
        // shaft construction; keeping it bounded to a few degrees removes the former perfect-cue
        // behaviour while remaining deterministic for multiplayer.
        var squirtFraction = shot.TipOffsetX / ShotInput.MaxTipOffset;
        var squirtAngle = squirtFraction * BilliardConstants.MaxSquirtAngle;
        var impactDirection = (direction * Math.Cos(squirtAngle)
                               - right * Math.Sin(squirtAngle)).Normalized();

        var radialDepth = Math.Sqrt(Math.Max(0.0,
            BilliardConstants.Radius * BilliardConstants.Radius - tangentOffset.LengthSquared));
        var contactOffset = tangentOffset - direction * radialDepth;

        var torqueArm = contactOffset.Cross(impactDirection);
        var inverseEffectiveMass = 1.0 / BilliardConstants.CueMass
                                   + 1.0 / BilliardConstants.Mass
                                   + torqueArm.LengthSquared / BilliardConstants.MomentOfInertia;
        var impulseMagnitude = (1.0 + BilliardConstants.CueTipRestitution)
                               * shot.Speed / inverseEffectiveMass;
        var impulse = impactDirection * impulseMagnitude;

        return new CueStrikeResult(
            impulse / BilliardConstants.Mass,
            contactOffset.Cross(impulse) / BilliardConstants.MomentOfInertia,
            impulseMagnitude);
    }

    public static double CentreBallSpeed(double cueSpeed)
    {
        return Calculate(new ShotInput(0.0, 0.0, cueSpeed, 0.0, 0.0)).LinearVelocity.Length;
    }
}
