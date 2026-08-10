using System;
using System.Collections.Generic;

namespace Pool.Simulation;

/// <summary>
/// Runs a whole shot from the cue strike to the last ball coming to rest, producing the event
/// timeline the rules engine reads and the sampled motion the presentation layer plays back.
///
/// Design note — why this is not the pure event-based solver pooltool uses: solving the exact
/// time of the next event needs quartic roots for ball-ball contact. That buys CPU efficiency,
/// which matters when an AI simulates thousands of candidate shots and is irrelevant for ten
/// balls in real time. Here the motion between events is still fully analytic (that is where the
/// behaviour comes from), but events are found by sweeping over short substeps using quadratics,
/// which is far easier to keep numerically robust. Swapping in a quartic solver later would
/// touch only this file.
/// </summary>
public sealed class ShotSimulator
{
    private const double MaxSubstep = 1.0 / 240.0;
    private const double MinSubstep = 1e-6;
    private const double PlaybackRate = 120.0;
    private const double MaxShotDuration = 30.0;
    private const double SimultaneousContactTolerance = 1e-7;
    private const int ContactSolverIterations = 16;

    private readonly TableSpec _table;

    public ShotSimulator(TableSpec table)
    {
        _table = table;
    }

    public ShotResult Simulate(IReadOnlyList<BallState> initialStates, int cueBallId, ShotInput shot)
    {
        var balls = new BallState[initialStates.Count];
        for (var i = 0; i < initialStates.Count; i++)
            balls[i] = initialStates[i].Clone();

        var events = new List<ShotEvent>();

        var cueBall = FindBall(balls, cueBallId);
        if (cueBall != null)
            ApplyCueStrike(cueBall, shot.Sanitized());

        return Run(balls, events);
    }

    private static BallState FindBall(BallState[] balls, int id)
    {
        foreach (var ball in balls)
        {
            if (ball.Id == id)
                return ball;
        }

        return null;
    }

    /// <summary>
    /// Instantaneous cue-stick/ball impact. CueStrikeModel converts cue speed into a finite
    /// impulse using cue mass, tip restitution and the rotational cost of an off-centre contact.
    /// An elevated cue drives the ball down into the cloth; the cloth response launches the jump.
    /// </summary>
    private static void ApplyCueStrike(BallState ball, ShotInput shot)
    {
        var strike = CueStrikeModel.Calculate(shot);
        if (strike.Impulse <= 0.0)
            return;

        ball.Velocity += strike.LinearVelocity;
        ball.AngularVelocity += strike.AngularVelocity;

        if (ball.Velocity.Y < 0.0 && ball.Position.Y <= BilliardConstants.Radius + 1e-9)
            BilliardMotion.ResolveClothBounce(ball);
        else
            BilliardMotion.RefreshMotionState(ball);
    }

    private ShotResult Run(BallState[] balls, List<ShotEvent> events)
    {
        var positions = new List<Vec3d>();
        var spins = new List<Vec3d>();
        var inPlay = new List<bool>();

        var frameInterval = 1.0 / PlaybackRate;
        var time = 0.0;
        var nextFrameTime = 0.0;
        var timedOut = false;

        RecordFrame(balls, positions, spins, inPlay);
        nextFrameTime += frameInterval;

        while (AnyMoving(balls))
        {
            if (time >= MaxShotDuration)
            {
                timedOut = true;
                break;
            }

            var dt = NextStepSize(balls);
            var collisions = FindEarliestCollisions(balls, dt, out var collisionTime);
            if (collisions.Count > 0)
                dt = Math.Max(collisionTime, MinSubstep);

            foreach (var ball in balls)
            {
                if (ball.IsMoving)
                    BilliardMotion.Evolve(ball, dt);
            }

            time += dt;

            // Pocket mouths are holes cut out of the rectangular cloth footprint. Capture them
            // before resolving a contact or a cloth landing at the same instant, otherwise an
            // overlapping jaw/floor marker can bounce a ball that has already entered the hole.
            HandlePocketsAndFalls(balls, time, events);

            if (collisions.Count > 0)
                ResolveCollisionBatch(balls, collisions, time, events);

            ResolveClothLandings(balls, time, events);

            foreach (var ball in balls)
            {
                if (!ball.InPlay)
                    continue;

                // Outside the supported bed there is no cloth beneath an airborne ball. Preserve
                // that state so it keeps falling instead of being snapped back onto an invisible
                // rectangular floor. Pocketed balls were already removed above.
                if (ball.Motion == BallMotion.Airborne
                    && !_table.HasClothSupport(ball.Position))
                    continue;

                BilliardMotion.RefreshMotionState(ball);
            }

            while (nextFrameTime <= time)
            {
                RecordFrame(balls, positions, spins, inPlay);
                nextFrameTime += frameInterval;
            }
        }

        RecordFrame(balls, positions, spins, inPlay);

        var playback = new ShotPlayback(frameInterval, balls.Length, positions, spins, inPlay);
        return new ShotResult(balls, events, playback, time, timedOut);
    }

    private static bool AnyMoving(BallState[] balls)
    {
        foreach (var ball in balls)
        {
            if (ball.IsMoving)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Step size is bounded three ways: a hard ceiling, the exact instant of the next motion
    /// state change (so the sliding-to-rolling transition lands precisely and the 5/7 rule stays
    /// exact), and the distance the fastest ball travels (keeping the constant-velocity
    /// assumption inside the collision sweeps honest).
    /// </summary>
    private static double NextStepSize(BallState[] balls)
    {
        var dt = MaxSubstep;
        var fastest = 0.0;

        foreach (var ball in balls)
        {
            if (!ball.IsMoving)
                continue;

            var toChange = BilliardMotion.TimeToStateChange(ball);
            if (toChange > MinSubstep && toChange < dt)
                dt = toChange;

            var speed = ball.Velocity.Length;
            if (speed > fastest)
                fastest = speed;
        }

        if (fastest > 0.0)
        {
            var travelLimit = BilliardConstants.Radius / fastest;
            if (travelLimit < dt)
                dt = travelLimit;
        }

        return Math.Max(dt, MinSubstep);
    }

    private readonly struct Collision
    {
        public readonly bool HasCollision;
        public readonly double Time;
        public readonly int BallA;
        public readonly int BallB;
        public readonly Vec3d CushionNormal;
        public readonly bool IsCushion;

        private Collision(bool has, double time, int ballA, int ballB, Vec3d normal, bool isCushion)
        {
            HasCollision = has;
            Time = time;
            BallA = ballA;
            BallB = ballB;
            CushionNormal = normal;
            IsCushion = isCushion;
        }

        public static readonly Collision None = new(false, 0, -1, -1, Vec3d.Zero, false);

        public static Collision Balls(double time, int a, int b) => new(true, time, a, b, Vec3d.Zero, false);
        public static Collision Cushion(double time, int ball, Vec3d normal) => new(true, time, ball, -1, normal, true);
    }

    private List<Collision> FindEarliestCollisions(BallState[] balls, double dt, out double bestTime)
    {
        var contacts = new List<Collision>();
        var earliest = double.PositiveInfinity;

        void Consider(Collision candidate)
        {
            if (candidate.Time < earliest - SimultaneousContactTolerance)
            {
                earliest = candidate.Time;
                contacts.Clear();
                contacts.Add(candidate);
            }
            else if (Math.Abs(candidate.Time - earliest) <= SimultaneousContactTolerance)
            {
                contacts.Add(candidate);
            }
        }

        for (var i = 0; i < balls.Length; i++)
        {
            var a = balls[i];
            if (!a.InPlay)
                continue;

            for (var j = i + 1; j < balls.Length; j++)
            {
                var b = balls[j];
                if (!b.InPlay)
                    continue;

                // Two stationary balls can never start touching within a step.
                if (!a.IsMoving && !b.IsMoving)
                    continue;

                if (!BilliardCollisions.SweepBallBall(a, b, dt, out var hitTime))
                    continue;

                Consider(Collision.Balls(hitTime, i, j));
            }

            if (!a.IsMoving)
                continue;

            foreach (var cushion in _table.Cushions)
            {
                if (!BilliardCollisions.SweepBallCushion(a, cushion, dt, out var cushionTime, out var normal))
                    continue;

                Consider(Collision.Cushion(cushionTime, i, normal));
            }
        }

        bestTime = earliest;
        return contacts;
    }

    /// <summary>
    /// Resolves contacts that occur at the same instant as one coupled constraint batch. A rack
    /// regularly gives the cue-facing ball two simultaneous neighbours; resolving only whichever
    /// pair happened to be enumerated first biases the break to one side. Projected sequential
    /// impulses converge the shared normal impulses before throw/spin is applied.
    /// </summary>
    private static void ResolveCollisionBatch(
        BallState[] balls,
        List<Collision> collisions,
        double time,
        List<ShotEvent> events)
    {
        var ballContacts = new List<Collision>();
        foreach (var collision in collisions)
        {
            if (collision.IsCushion)
            {
                var ball = balls[collision.BallA];
                if (!ball.InPlay)
                    continue;

                BilliardCollisions.ResolveCushion(ball, collision.CushionNormal);
                events.Add(new ShotEvent(time, ShotEventType.BallHitCushion, ball.Id));
            }
            else
            {
                if (!balls[collision.BallA].InPlay || !balls[collision.BallB].InPlay)
                    continue;

                ballContacts.Add(collision);
            }
        }

        if (ballContacts.Count == 0)
            return;

        var normals = new Vec3d[ballContacts.Count];
        var targetSpeeds = new double[ballContacts.Count];
        var impactSpeeds = new double[ballContacts.Count];
        var accumulatedImpulses = new double[ballContacts.Count];

        for (var i = 0; i < ballContacts.Count; i++)
        {
            var contact = ballContacts[i];
            var a = balls[contact.BallA];
            var b = balls[contact.BallB];
            var normal = (b.Position - a.Position).Normalized();
            normals[i] = normal;

            var incomingSpeed = (b.Velocity - a.Velocity).Dot(normal);
            impactSpeeds[i] = (b.Velocity - a.Velocity).Length;
            targetSpeeds[i] = incomingSpeed < 0.0
                ? -BilliardConstants.BallBallRestitution * incomingSpeed
                : 0.0;
        }

        var inverseEffectiveMass = 2.0 / BilliardConstants.Mass;
        for (var iteration = 0; iteration < ContactSolverIterations; iteration++)
        {
            // Alternating direction removes the remaining preference for array order while
            // preserving deterministic results on every peer.
            var reverse = (iteration & 1) != 0;
            for (var step = 0; step < ballContacts.Count; step++)
            {
                var i = reverse ? ballContacts.Count - 1 - step : step;
                var contact = ballContacts[i];
                var normal = normals[i];
                if (normal.LengthSquared <= 0.0)
                    continue;

                var a = balls[contact.BallA];
                var b = balls[contact.BallB];
                var currentSpeed = (b.Velocity - a.Velocity).Dot(normal);
                var impulseDelta = (targetSpeeds[i] - currentSpeed) / inverseEffectiveMass;
                var newImpulse = Math.Max(0.0, accumulatedImpulses[i] + impulseDelta);
                impulseDelta = newImpulse - accumulatedImpulses[i];
                accumulatedImpulses[i] = newImpulse;

                a.Velocity -= normal * (impulseDelta / BilliardConstants.Mass);
                b.Velocity += normal * (impulseDelta / BilliardConstants.Mass);
            }
        }

        // Friction is evaluated from one shared post-normal snapshot and accumulated, rather
        // than letting the first tangential contact mutate the velocity seen by the second.
        // This matters when the head ball meets both balls behind it at the same instant.
        var velocityDeltas = new Vec3d[balls.Length];
        var spinDeltas = new Vec3d[balls.Length];
        for (var i = 0; i < ballContacts.Count; i++)
        {
            var contact = ballContacts[i];
            var a = balls[contact.BallA];
            var b = balls[contact.BallB];

            var probeA = a.Clone();
            var probeB = b.Clone();
            BilliardCollisions.ApplyBallBallFriction(
                probeA, probeB, normals[i], accumulatedImpulses[i], impactSpeeds[i]);

            velocityDeltas[contact.BallA] += probeA.Velocity - a.Velocity;
            velocityDeltas[contact.BallB] += probeB.Velocity - b.Velocity;
            spinDeltas[contact.BallA] += probeA.AngularVelocity - a.AngularVelocity;
            spinDeltas[contact.BallB] += probeB.AngularVelocity - b.AngularVelocity;
        }

        for (var i = 0; i < balls.Length; i++)
        {
            balls[i].Velocity += velocityDeltas[i];
            balls[i].AngularVelocity += spinDeltas[i];
        }

        foreach (var contact in ballContacts)
        {
            var a = balls[contact.BallA];
            var b = balls[contact.BallB];
            BilliardCollisions.SeparateOverlap(a, b);
            events.Add(new ShotEvent(time, ShotEventType.BallHitBall, a.Id, b.Id));
        }
    }

    /// <summary>
    /// A ball thrown up by a jump shot comes back down; the cloth bounce is what actually gets it
    /// over an intervening ball. Without this the ball would just be clamped flat on landing and
    /// jump shots would die on contact.
    /// </summary>
    private void ResolveClothLandings(BallState[] balls, double time, List<ShotEvent> events)
    {
        foreach (var ball in balls)
        {
            if (ball.Motion != BallMotion.Airborne)
                continue;

            if (ball.Position.Y > BilliardConstants.Radius + 1e-9 || ball.Velocity.Y >= 0.0)
                continue;

            // Outside the bed, and inside every pocket cutout, there is no cloth to land on.
            // Keep falling rather than bouncing on the marker's rectangular bounding box.
            if (!_table.HasClothSupport(ball.Position))
                continue;

            BilliardMotion.ResolveClothBounce(ball);
            events.Add(new ShotEvent(time, ShotEventType.BallHitCloth, ball.Id));
        }
    }

    private void HandlePocketsAndFalls(BallState[] balls, double time, List<ShotEvent> events)
    {
        foreach (var ball in balls)
        {
            if (!ball.InPlay)
                continue;

            if (BilliardCollisions.IsOverPocket(ball, _table, out var pocketIndex))
            {
                ball.Motion = BallMotion.Pocketed;
                ball.Velocity = Vec3d.Zero;
                ball.AngularVelocity = Vec3d.Zero;
                events.Add(new ShotEvent(time, ShotEventType.BallPocketed, ball.Id, pocketIndex));
                continue;
            }

            // Off the playing surface and below the cloth means it left the table entirely — a
            // different foul from potting in most rulesets, so it gets its own event.
            var belowCloth = ball.Position.Y < BilliardConstants.Radius - _table.DropDepth;

            if (!_table.IsOverPlaySurface(ball.Position, BilliardConstants.Radius) && belowCloth)
            {
                ball.Motion = BallMotion.Pocketed;
                ball.Velocity = Vec3d.Zero;
                ball.AngularVelocity = Vec3d.Zero;
                events.Add(new ShotEvent(time, ShotEventType.BallOffTable, ball.Id));
            }
        }
    }

    private static void RecordFrame(
        BallState[] balls,
        List<Vec3d> positions,
        List<Vec3d> spins,
        List<bool> inPlay)
    {
        foreach (var ball in balls)
        {
            positions.Add(ball.Position);
            spins.Add(ball.AngularVelocity);
            inPlay.Add(ball.InPlay);
        }
    }
}
