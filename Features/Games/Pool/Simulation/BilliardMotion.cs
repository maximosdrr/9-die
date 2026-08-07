using System;

namespace Pool.Simulation;

/// <summary>
/// Closed-form evolution of a single ball between events. This is where the "feel" of the game
/// comes from — everything a player recognises as pool behaviour (stun, draw, follow, a ball that
/// actually stops) falls out of the four motion states below rather than being tuned in.
///
/// The two equations that matter most:
///
///   Sliding — the contact-point velocity u = v + R(k̂ × ω) decays at a constant (7/2)·μ_s·g,
///   always along its initial direction. When it reaches zero the ball is in natural roll, and
///   for a centre-struck ball that happens at exactly 5/7 of the launch speed. That 28.6% loss
///   is the mechanism behind stun and drag; a generic solver has no equivalent.
///
///   Rolling — deceleration is a CONSTANT μ_r·g ≈ 0.098 m/s², not proportional to speed. A ball
///   at 1 m/s therefore travels 5.1 m and stops after 10.2 s, exactly. Engine damping decays
///   exponentially, which both over-brakes fast balls and lets slow ones creep forever.
/// </summary>
public static class BilliardMotion
{
    private const double SlidingDecel = 3.5 * BilliardConstants.SlidingFriction * BilliardConstants.Gravity;
    private const double RollingDecel = BilliardConstants.RollingFriction * BilliardConstants.Gravity;
    private const double SpinDecel = 5.0 * BilliardConstants.SpinningFriction * BilliardConstants.Gravity
                                     / (2.0 * BilliardConstants.Radius);
    private const double SlidingLinearDecel = BilliardConstants.SlidingFriction * BilliardConstants.Gravity;
    private const double SlidingAngularAccel = 5.0 * BilliardConstants.SlidingFriction * BilliardConstants.Gravity
                                               / (2.0 * BilliardConstants.Radius);

    /// <summary>
    /// Time until this ball's motion state changes on its own (ignoring collisions). The solver
    /// clamps each substep to this so transitions land exactly on their analytic instant rather
    /// than being smeared across a step — which is what keeps the 5/7 rule exact.
    /// </summary>
    public static double TimeToStateChange(BallState ball)
    {
        switch (ball.Motion)
        {
            case BallMotion.Sliding:
                return ball.ContactPointVelocity().FlatLength / SlidingDecel;

            case BallMotion.Rolling:
                return ball.Velocity.FlatLength / RollingDecel;

            case BallMotion.Spinning:
                return Math.Abs(ball.AngularVelocity.Y) / SpinDecel;

            case BallMotion.Airborne:
                return TimeToLand(ball);

            default:
                return double.PositiveInfinity;
        }
    }

    private static double TimeToLand(BallState ball)
    {
        var heightAboveRest = ball.Position.Y - BilliardConstants.Radius;
        var verticalVelocity = ball.Velocity.Y;

        var discriminant = verticalVelocity * verticalVelocity
                           + 2.0 * BilliardConstants.Gravity * heightAboveRest;
        if (discriminant < 0.0)
            return double.PositiveInfinity;

        return (verticalVelocity + Math.Sqrt(discriminant)) / BilliardConstants.Gravity;
    }

    /// <summary>Advances a ball by dt while staying inside its current motion state.</summary>
    public static void Evolve(BallState ball, double dt)
    {
        if (dt <= 0.0)
            return;

        switch (ball.Motion)
        {
            case BallMotion.Sliding:
                EvolveSliding(ball, dt);
                break;
            case BallMotion.Rolling:
                EvolveRolling(ball, dt);
                break;
            case BallMotion.Spinning:
                DecaySpin(ball, dt);
                break;
            case BallMotion.Airborne:
                EvolveAirborne(ball, dt);
                break;
        }
    }

    private static void EvolveSliding(BallState ball, double dt)
    {
        // The friction direction is fixed for the whole sliding phase: u only ever shrinks along
        // its own direction, it never turns. That is what makes the closed form valid.
        var slipDirection = ball.ContactPointVelocity().Normalized();

        ball.Position += ball.Velocity * dt - slipDirection * (0.5 * SlidingLinearDecel * dt * dt);
        ball.Velocity -= slipDirection * (SlidingLinearDecel * dt);

        // Friction at the contact point also torques the ball; k̂ × û is horizontal, so this
        // touches only the rolling component and leaves vertical spin to DecaySpin.
        var angularAccel = Vec3d.Up.Cross(slipDirection) * SlidingAngularAccel;
        ball.AngularVelocity += angularAccel * dt;

        DecaySpin(ball, dt);
    }

    private static void EvolveRolling(BallState ball, double dt)
    {
        var direction = ball.Velocity.Flat.Normalized();

        ball.Position += ball.Velocity * dt - direction * (0.5 * RollingDecel * dt * dt);
        ball.Velocity -= direction * (RollingDecel * dt);

        DecaySpin(ball, dt);
        LockRollingSpin(ball);
    }

    private static void EvolveAirborne(BallState ball, double dt)
    {
        var drop = 0.5 * BilliardConstants.Gravity * dt * dt;
        ball.Position += ball.Velocity * dt - new Vec3d(0.0, drop, 0.0);
        ball.Velocity -= new Vec3d(0.0, BilliardConstants.Gravity * dt, 0.0);
        // No contact means no friction: angular velocity is carried unchanged through the flight.
    }

    private static void DecaySpin(BallState ball, double dt)
    {
        var spin = ball.AngularVelocity.Y;
        if (spin == 0.0)
            return;

        var magnitude = Math.Abs(spin) - SpinDecel * dt;
        if (magnitude < 0.0)
            magnitude = 0.0;

        ball.AngularVelocity = ball.AngularVelocity.WithY(Math.Sign(spin) * magnitude);
    }

    /// <summary>
    /// In natural roll the horizontal spin is not free — it is determined by the velocity via
    /// ω_h = (k̂ × v) / R. Re-imposing it every step stops integration drift from slowly breaking
    /// the rolling constraint.
    /// </summary>
    private static void LockRollingSpin(BallState ball)
    {
        var rolling = Vec3d.Up.Cross(ball.Velocity.Flat) / BilliardConstants.Radius;
        ball.AngularVelocity = rolling.WithY(ball.AngularVelocity.Y);
    }

    /// <summary>
    /// Re-derives the motion state after a step or a collision, snapping the ball onto the exact
    /// state boundary so the next closed-form segment starts clean.
    /// </summary>
    public static void RefreshMotionState(BallState ball)
    {
        if (ball.Motion == BallMotion.Pocketed)
            return;

        if (ball.Position.Y > BilliardConstants.Radius + 1e-9)
        {
            ball.Motion = BallMotion.Airborne;
            return;
        }

        // On the cloth: clamp away any residual vertical motion left by a landing.
        if (ball.Motion == BallMotion.Airborne || ball.Velocity.Y != 0.0)
        {
            ball.Position = ball.Position.WithY(BilliardConstants.Radius);
            ball.Velocity = ball.Velocity.WithY(0.0);
        }

        var slip = ball.ContactPointVelocity().FlatLength;
        if (slip > BilliardConstants.SlidingEpsilon)
        {
            ball.Motion = BallMotion.Sliding;
            return;
        }

        var speed = ball.Velocity.FlatLength;
        if (speed > BilliardConstants.StopEpsilon)
        {
            ball.Motion = BallMotion.Rolling;
            LockRollingSpin(ball);
            return;
        }

        ball.Velocity = Vec3d.Zero;

        if (Math.Abs(ball.AngularVelocity.Y) > BilliardConstants.SpinStopEpsilon)
        {
            ball.AngularVelocity = new Vec3d(0.0, ball.AngularVelocity.Y, 0.0);
            ball.Motion = BallMotion.Spinning;
            return;
        }

        ball.AngularVelocity = Vec3d.Zero;
        ball.Motion = BallMotion.Stationary;
    }

    /// <summary>
    /// Bounce off the cloth for a ball coming down from a jump shot. Only the vertical component
    /// is reflected; the horizontal is left to sliding friction on the next step.
    /// </summary>
    public static void ResolveClothBounce(BallState ball)
    {
        ball.Position = ball.Position.WithY(BilliardConstants.Radius);

        var verticalSpeed = -ball.Velocity.Y * BilliardConstants.BallClothRestitution;
        if (verticalSpeed < 0.05)
        {
            // Too slow to leave the cloth again — settle instead of buzzing through ever-smaller
            // bounces, which would generate events forever.
            ball.Velocity = ball.Velocity.WithY(0.0);
            RefreshMotionState(ball);
            return;
        }

        ball.Velocity = ball.Velocity.WithY(verticalSpeed);
        ball.Motion = BallMotion.Airborne;
    }
}
