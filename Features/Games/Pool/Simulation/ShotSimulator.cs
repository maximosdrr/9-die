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
    /// Instantaneous-impulse stick-ball model. The cue delivers m·V along its axis at a contact
    /// point offset from centre by the tip position; the resulting torque about the centre is
    /// what produces draw, follow and english. An elevated cue drives the ball down into the
    /// cloth, and the cloth bounce is what actually launches a jump shot — the same mechanism as
    /// reality, rather than the old code's separate "jump efficiency" factor.
    /// </summary>
    private static void ApplyCueStrike(BallState ball, ShotInput shot)
    {
        if (shot.Speed <= 0.0)
            return;

        var horizontal = new Vec3d(Math.Sin(shot.AimYaw), 0.0, Math.Cos(shot.AimYaw));
        var direction = (horizontal * Math.Cos(shot.Elevation)
                         - Vec3d.Up * Math.Sin(shot.Elevation)).Normalized();

        // Ball-local frame for the tip offset: right is across the aim line, up is perpendicular
        // to both, so an elevated cue's offsets stay square to the cue rather than to the table.
        var right = direction.Cross(Vec3d.Up).Normalized();
        if (right.LengthSquared <= 0.0)
            right = new Vec3d(1.0, 0.0, 0.0);
        var up = right.Cross(direction).Normalized();

        var offsetX = shot.TipOffsetX * BilliardConstants.Radius;
        var offsetY = shot.TipOffsetY * BilliardConstants.Radius;
        var contactOffset = right * offsetX + up * offsetY;

        var impulse = direction * (BilliardConstants.Mass * shot.Speed);

        ball.Velocity += impulse / BilliardConstants.Mass;
        ball.AngularVelocity += contactOffset.Cross(impulse) / BilliardConstants.MomentOfInertia;

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
            var collision = FindEarliestCollision(balls, dt);
            if (collision.HasCollision)
                dt = Math.Max(collision.Time, MinSubstep);

            foreach (var ball in balls)
            {
                if (ball.IsMoving)
                    BilliardMotion.Evolve(ball, dt);
            }

            time += dt;

            if (collision.HasCollision)
                ResolveCollision(balls, collision, time, events);

            ResolveClothLandings(balls, time, events);

            foreach (var ball in balls)
            {
                if (ball.InPlay)
                    BilliardMotion.RefreshMotionState(ball);
            }

            HandlePocketsAndFalls(balls, time, events);

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

    private Collision FindEarliestCollision(BallState[] balls, double dt)
    {
        var best = Collision.None;
        var bestTime = double.PositiveInfinity;

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

                if (hitTime >= bestTime)
                    continue;

                bestTime = hitTime;
                best = Collision.Balls(hitTime, i, j);
            }

            if (!a.IsMoving)
                continue;

            foreach (var cushion in _table.Cushions)
            {
                if (!BilliardCollisions.SweepBallCushion(a, cushion, dt, out var cushionTime, out var normal))
                    continue;

                if (cushionTime >= bestTime)
                    continue;

                bestTime = cushionTime;
                best = Collision.Cushion(cushionTime, i, normal);
            }
        }

        return best;
    }

    private static void ResolveCollision(
        BallState[] balls,
        Collision collision,
        double time,
        List<ShotEvent> events)
    {
        if (collision.IsCushion)
        {
            var ball = balls[collision.BallA];
            BilliardCollisions.ResolveCushion(ball, collision.CushionNormal);
            events.Add(new ShotEvent(time, ShotEventType.BallHitCushion, ball.Id));
            return;
        }

        var a = balls[collision.BallA];
        var b = balls[collision.BallB];
        BilliardCollisions.ResolveBallBall(a, b);
        events.Add(new ShotEvent(time, ShotEventType.BallHitBall, a.Id, b.Id));
    }

    /// <summary>
    /// A ball thrown up by a jump shot comes back down; the cloth bounce is what actually gets it
    /// over an intervening ball. Without this the ball would just be clamped flat on landing and
    /// jump shots would die on contact.
    /// </summary>
    private static void ResolveClothLandings(BallState[] balls, double time, List<ShotEvent> events)
    {
        foreach (var ball in balls)
        {
            if (ball.Motion != BallMotion.Airborne)
                continue;

            if (ball.Position.Y > BilliardConstants.Radius + 1e-9 || ball.Velocity.Y >= 0.0)
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
