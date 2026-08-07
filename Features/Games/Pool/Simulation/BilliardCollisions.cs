using System;

namespace Pool.Simulation;

/// <summary>
/// Collision detection (swept, so nothing tunnels) and response for balls, cushions and pockets.
///
/// The response models are what a generic contact solver cannot express:
///
///   Ball-ball carries a tangential friction impulse (μ_b). It is small, but it is the entire
///   mechanism behind throw — the reason a cut shot does not send the object ball exactly along
///   the line of centres, and the reason sidespin transfers.
///
///   Ball-cushion follows Han 2005: the nose sits ABOVE the ball centre, so the contact normal
///   points away from the cushion AND downward. That is why a real cushion presses the ball into
///   the cloth instead of launching it, and why rebound angle depends on incoming spin — running
///   english lengthens the rebound, reverse english shortens it.
/// </summary>
public static class BilliardCollisions
{
    private const double Diameter = 2.0 * BilliardConstants.Radius;
    private const double DiameterSquared = Diameter * Diameter;

    // Ball centre height at the cushion nose: sinθ = ε/R, so the contact sits R·cosθ away
    // horizontally rather than a full radius.
    private static readonly double NoseSin = BilliardConstants.CushionNoseHeightRatio;
    private static readonly double NoseCos = Math.Sqrt(1.0 - NoseSin * NoseSin);
    private static readonly double CushionContactDistance = BilliardConstants.Radius * NoseCos;
    private const double CushionTopHeight = 2.0 * BilliardConstants.Radius;

    /// <summary>
    /// Smallest time in (0, dt] at which two balls touch, treating velocities as constant over
    /// the step. Returns false if they do not meet within the step. Velocity is near enough to
    /// constant here because friction changes it by well under a millimetre per millisecond.
    /// </summary>
    public static bool SweepBallBall(BallState a, BallState b, double dt, out double hitTime)
    {
        hitTime = 0.0;

        var relativePosition = b.Position - a.Position;
        var relativeVelocity = b.Velocity - a.Velocity;

        var quadratic = relativeVelocity.LengthSquared;
        if (quadratic <= 0.0)
            return false;

        var linear = 2.0 * relativePosition.Dot(relativeVelocity);
        var constant = relativePosition.LengthSquared - DiameterSquared;

        // Already touching and separating — let them go rather than resolving twice.
        if (constant <= 0.0 && linear >= 0.0)
            return false;

        if (constant <= 0.0)
        {
            hitTime = 0.0;
            return true;
        }

        var discriminant = linear * linear - 4.0 * quadratic * constant;
        if (discriminant < 0.0)
            return false;

        var root = (-linear - Math.Sqrt(discriminant)) / (2.0 * quadratic);
        if (root < 0.0 || root > dt)
            return false;

        hitTime = root;
        return true;
    }

    /// <summary>
    /// Smallest time in (0, dt] at which the ball reaches a cushion, either on its face or on a
    /// jaw (segment endpoint). The jaw case is what makes a ball rattle in the pocket mouth
    /// instead of sliding through the gap.
    /// </summary>
    public static bool SweepBallCushion(
        BallState ball,
        CushionSegment cushion,
        double dt,
        out double hitTime,
        out Vec3d contactNormal)
    {
        hitTime = 0.0;
        contactNormal = cushion.Normal;

        var approachSpeed = ball.Velocity.Dot(cushion.Normal);

        // Face hit: the centre crosses the plane offset by the nose contact distance.
        if (approachSpeed < 0.0)
        {
            var distance = (ball.Position - cushion.Start).Dot(cushion.Normal);
            var time = (CushionContactDistance - distance) / approachSpeed;

            if (time >= 0.0 && time <= dt && !ClearsCushionAt(ball, time))
            {
                var contactPoint = ball.Position + ball.Velocity * time;
                if (ProjectsOntoSegment(contactPoint, cushion))
                {
                    hitTime = time;
                    contactNormal = cushion.Normal;
                    return true;
                }
            }
        }

        // Jaw hits: treat each endpoint as a vertical post of zero radius.
        var hitJaw = false;
        var bestTime = double.PositiveInfinity;
        var bestNormal = cushion.Normal;

        foreach (var jaw in new[] { cushion.Start, cushion.End })
        {
            if (!SweepBallPoint(ball, jaw, dt, out var jawTime))
                continue;

            if (ClearsCushionAt(ball, jawTime))
                continue;

            if (jawTime >= bestTime)
                continue;

            var contactPoint = ball.Position + ball.Velocity * jawTime;
            var normal = (contactPoint - jaw).Flat.Normalized();
            if (normal.FlatLengthSquared <= 0.0)
                continue;

            bestTime = jawTime;
            bestNormal = normal;
            hitJaw = true;
        }

        if (!hitJaw)
            return false;

        hitTime = bestTime;
        contactNormal = bestNormal;
        return true;
    }

    private static bool ClearsCushionAt(BallState ball, double time)
    {
        if (ball.Motion != BallMotion.Airborne)
            return false;

        var centreHeight = ball.Position.Y + ball.Velocity.Y * time
                           - 0.5 * BilliardConstants.Gravity * time * time;
        return centreHeight - BilliardConstants.Radius > CushionTopHeight;
    }

    private static bool ProjectsOntoSegment(Vec3d point, CushionSegment cushion)
    {
        var along = cushion.End - cushion.Start;
        var lengthSquared = along.FlatLengthSquared;
        if (lengthSquared <= 0.0)
            return false;

        var t = (point - cushion.Start).Flat.Dot(along.Flat) / lengthSquared;
        return t >= 0.0 && t <= 1.0;
    }

    private static bool SweepBallPoint(BallState ball, Vec3d point, double dt, out double hitTime)
    {
        hitTime = 0.0;

        var relativePosition = (ball.Position - point).Flat;
        var velocity = ball.Velocity.Flat;

        var quadratic = velocity.LengthSquared;
        if (quadratic <= 0.0)
            return false;

        var linear = 2.0 * relativePosition.Dot(velocity);
        var constant = relativePosition.LengthSquared
                       - BilliardConstants.Radius * BilliardConstants.Radius;

        if (constant <= 0.0 && linear >= 0.0)
            return false;

        var discriminant = linear * linear - 4.0 * quadratic * constant;
        if (discriminant < 0.0)
            return false;

        var root = (-linear - Math.Sqrt(discriminant)) / (2.0 * quadratic);
        if (root < 0.0 || root > dt)
            return false;

        hitTime = root;
        return true;
    }

    /// <summary>
    /// Equal-mass collision with restitution plus a tangential friction impulse. The tangential
    /// part is capped both by the Coulomb cone (μ_b · P_n) and by the impulse that would exactly
    /// stop the surfaces slipping — for two spheres that limit is (m/7)·|slip|, since a tangential
    /// impulse changes the relative surface velocity by 7P/m.
    /// </summary>
    public static void ResolveBallBall(BallState a, BallState b)
    {
        var normal = (b.Position - a.Position).Normalized();
        if (normal.LengthSquared <= 0.0)
            return;

        var relativeVelocity = b.Velocity - a.Velocity;
        var normalSpeed = relativeVelocity.Dot(normal);
        if (normalSpeed >= 0.0)
            return;

        // Reduced mass for two equal masses is m/2.
        var normalImpulse = -(1.0 + BilliardConstants.BallBallRestitution)
                            * (BilliardConstants.Mass * 0.5) * normalSpeed;

        a.Velocity -= normal * (normalImpulse / BilliardConstants.Mass);
        b.Velocity += normal * (normalImpulse / BilliardConstants.Mass);

        ApplyBallBallFriction(a, b, normal, normalImpulse, relativeVelocity.Length);
        SeparateOverlap(a, b);
    }

    /// <summary>
    /// Tangential half of the ball contact response. Kept separate so a simultaneous-contact
    /// batch can solve all coupled normal impulses first and add throw/spin afterwards.
    /// </summary>
    internal static void ApplyBallBallFriction(
        BallState a, BallState b, Vec3d normal, double normalImpulse, double impactSpeed)
    {
        if (normalImpulse <= 0.0)
            return;

        // Slip of b's surface against a's surface at the contact point.
        var contactOnA = normal * BilliardConstants.Radius;
        var contactOnB = -normal * BilliardConstants.Radius;
        var surfaceVelocityA = a.Velocity + a.AngularVelocity.Cross(contactOnA);
        var surfaceVelocityB = b.Velocity + b.AngularVelocity.Cross(contactOnB);

        var slip = surfaceVelocityB - surfaceVelocityA;
        slip -= normal * slip.Dot(normal);

        var slipSpeed = slip.Length;
        if (slipSpeed <= 1e-9)
            return;

        var slipDirection = slip / slipSpeed;
        var stoppingImpulse = BilliardConstants.Mass * slipSpeed / 7.0;
        var friction = BallBallFrictionForSpeed(impactSpeed);
        var frictionImpulse = Math.Min(friction * normalImpulse, stoppingImpulse);

        a.Velocity += slipDirection * (frictionImpulse / BilliardConstants.Mass);
        b.Velocity -= slipDirection * (frictionImpulse / BilliardConstants.Mass);

        // Both balls receive the same angular change — the two surfaces rub against each other.
        var spinChange = normal.Cross(slipDirection)
                         * (BilliardConstants.Radius * frictionImpulse / BilliardConstants.MomentOfInertia);
        a.AngularVelocity += spinChange;
        b.AngularVelocity += spinChange;
    }

    /// <summary>
    /// Ball-ball friction falls sharply as impact speed rises. A constant coefficient makes hard
    /// shots grab far too much and soft touch shots not enough; this is Alciatore's measured fit.
    /// </summary>
    public static double BallBallFrictionForSpeed(double relativeSpeed)
    {
        var speed = Math.Max(0.0, relativeSpeed);
        return BilliardConstants.BallBallFrictionA
               + BilliardConstants.BallBallFrictionB
               * Math.Exp(-BilliardConstants.BallBallFrictionC * speed);
    }

    internal static void SeparateOverlap(BallState a, BallState b)
    {
        var delta = b.Position - a.Position;
        var distance = delta.Length;
        if (distance <= 0.0 || distance >= Diameter)
            return;

        var correction = delta.Normalized() * ((Diameter - distance) * 0.5 + 1e-9);
        a.Position -= correction;
        b.Position += correction;
    }

    /// <summary>
    /// Han 2005 cushion response. `inwardNormal` points from the cushion into the table.
    ///
    /// The contact sits above the ball centre by ε = CushionNoseHeightRatio · R, so the impulse
    /// direction is tilted downward. That downward component is exactly what the old rail
    /// geometry had inverted — those rails pushed the ball UP by ~27% of the rebound speed on
    /// every contact.
    /// </summary>
    public static void ResolveCushion(BallState ball, Vec3d inwardNormal)
    {
        var normal = inwardNormal.Flat.Normalized();
        if (normal.FlatLengthSquared <= 0.0)
            return;

        // Direction from the ball centre to the contact point: toward the cushion and upward.
        var towardCushion = -normal;
        var contactDirection = (towardCushion * NoseCos + Vec3d.Up * NoseSin).Normalized();

        var approachSpeed = ball.Velocity.Dot(contactDirection);
        if (approachSpeed <= 0.0)
            return;

        var normalImpulse = BilliardConstants.Mass
                            * (1.0 + BilliardConstants.CushionRestitution) * approachSpeed;

        var contactOffset = contactDirection * BilliardConstants.Radius;
        var surfaceVelocity = ball.Velocity + ball.AngularVelocity.Cross(contactOffset);
        var slip = surfaceVelocity - contactDirection * surfaceVelocity.Dot(contactDirection);
        var slipSpeed = slip.Length;

        ball.Velocity -= contactDirection * (normalImpulse / BilliardConstants.Mass);

        if (slipSpeed > 1e-9)
        {
            var slipDirection = slip / slipSpeed;

            // For a sphere against a fixed surface a tangential impulse changes the surface
            // velocity by 3.5P/m, so (2/7)·m·|slip| is what fully kills the slip.
            var stoppingImpulse = 2.0 / 7.0 * BilliardConstants.Mass * slipSpeed;
            var frictionImpulse = Math.Min(BilliardConstants.CushionFriction * normalImpulse, stoppingImpulse);

            ball.Velocity -= slipDirection * (frictionImpulse / BilliardConstants.Mass);
            ball.AngularVelocity -= contactOffset.Cross(slipDirection)
                                    * (frictionImpulse / BilliardConstants.MomentOfInertia);
        }

        // The cloth holds the ball up; the downward part of the impulse must not drive it under.
        if (ball.Position.Y <= BilliardConstants.Radius + 1e-9 && ball.Velocity.Y < 0.0)
            ball.Velocity = ball.Velocity.WithY(0.0);
    }

    /// <summary>True when the ball's centre has entered a pocket mouth and it should drop.</summary>
    public static bool IsOverPocket(BallState ball, TableSpec table, out int pocketIndex)
    {
        return table.TryGetPocketAt(ball.Position, out pocketIndex);
    }
}
