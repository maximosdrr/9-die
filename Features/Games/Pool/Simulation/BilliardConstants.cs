using System;

namespace Pool.Simulation;

/// <summary>
/// Physical parameters for the billiard model. Values follow pooltool's default ball parameters
/// (ekiefl/pooltool, pooltool/objects/ball/params.py), which in turn derive from the measurements
/// collected at billiards.colostate.edu (Dr. Dave Alciatore).
///
/// These are real-world quantities, not tuning knobs — changing them changes what the game claims
/// to be simulating. The knobs a designer should reach for live in TableSpec (geometry) and in the
/// cue's power mapping, not here.
/// </summary>
public static class BilliardConstants
{
    /// <summary>Ball mass, kg. Regulation pool ball.</summary>
    public const double Mass = 0.170097;

    /// <summary>Ball radius, m. Regulation 2.25 in diameter.</summary>
    public const double Radius = 0.028575;

    public const double Gravity = 9.81;

    /// <summary>Mass of the complete playing cue, kg (approximately 20 oz).</summary>
    public const double CueMass = 0.567;

    /// <summary>Effective normal restitution of the leather cue tip against the ball.</summary>
    public const double CueTipRestitution = 0.85;

    /// <summary>
    /// Maximum initial cue-ball deflection at the legal side-spin limit. Real cues vary; two
    /// degrees represents a modern low-deflection shaft without pretending squirt is zero.
    /// </summary>
    public const double MaxSquirtAngle = 2.0 * Math.PI / 180.0;

    /// <summary>
    /// Sliding (kinetic) friction between ball and cloth. Governs how fast a struck ball sheds
    /// the relative velocity at its contact point and settles into natural roll.
    /// </summary>
    public const double SlidingFriction = 0.2;

    /// <summary>
    /// Rolling resistance between ball and cloth. Produces a CONSTANT deceleration of
    /// RollingFriction * Gravity ≈ 0.098 m/s², which is what makes a rolling ball actually come
    /// to rest at a predictable distance. An engine's velocity-proportional damping cannot
    /// reproduce this — it decays exponentially and never reaches zero, which is the "floaty"
    /// feel the old RigidBody3D implementation had.
    /// </summary>
    public const double RollingFriction = 0.01;

    /// <summary>
    /// Spinning friction, with the ball radius already factored out (pooltool's
    /// u_sp_proportionality = 10 * 2 / 5 / 9). Multiply by Radius for the actual coefficient.
    /// Governs how long pure z-axis spin ("english" left on the ball) survives.
    /// </summary>
    public const double SpinningFrictionProportionality = 10.0 * 2.0 / 5.0 / 9.0;

    public const double SpinningFriction = SpinningFrictionProportionality * Radius;

    /// <summary>
    /// Tangential friction between two balls. Small, but it is the entire mechanism behind
    /// cut-induced and spin-induced throw — the reason a cut shot does not send the object ball
    /// exactly along the line of centres. Absent from any generic contact solver.
    /// </summary>
    /// <summary>Alciatore's measured speed-dependent ball-ball friction fit.</summary>
    public const double BallBallFrictionA = 0.009951;
    public const double BallBallFrictionB = 0.108;
    public const double BallBallFrictionC = 1.088;

    /// <summary>Restitution between two balls. Polished phenolic is nearly, but not perfectly, elastic.</summary>
    public const double BallBallRestitution = 0.95;

    /// <summary>Restitution between ball and cloth, used when an airborne ball lands.</summary>
    public const double BallClothRestitution = 0.5;

    /// <summary>Restitution at the cushion, as an effective lumped value for the Han 2005 model.</summary>
    public const double CushionRestitution = 0.85;

    /// <summary>Friction at the cushion. With the nose above centre, this is what converts incoming sidespin into a changed rebound angle.</summary>
    public const double CushionFriction = 0.2;

    /// <summary>
    /// Height of the cushion nose above the ball's centre, as a fraction of the radius. Sources
    /// disagree: Mathavan 2010 derives 7R/5 above the cloth (0.4R above centre) while ekiefl
    /// reports 0.1R-0.2R in practice, and WPA equipment specs sit between them. 0.15R is taken
    /// from the practical end of that range; it is the one geometric value here worth revisiting
    /// if cushion rebound feels wrong.
    /// </summary>
    public const double CushionNoseHeightRatio = 0.15;

    /// <summary>
    /// Moment of inertia of a solid sphere: (2/5)mR². Precomputed because it appears in every
    /// angular impulse.
    /// </summary>
    public const double MomentOfInertia = 0.4 * Mass * Radius * Radius;

    /// <summary>
    /// Below this contact-point relative speed a sliding ball is treated as having reached
    /// natural roll. Chosen well under the resolution any shot outcome depends on.
    /// </summary>
    public const double SlidingEpsilon = 1e-5;

    /// <summary>Below this speed a rolling ball is snapped to a full stop.</summary>
    public const double StopEpsilon = 1e-4;

    /// <summary>Below this angular speed residual spin is snapped to zero.</summary>
    public const double SpinStopEpsilon = 1e-3;
}
