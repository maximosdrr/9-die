using System.Collections.Generic;

namespace Pool.Simulation;

public enum ShotEventType
{
    BallHitBall,
    BallHitCushion,

    /// <summary>An airborne ball landed back on the cloth — the jump-shot bounce.</summary>
    BallHitCloth,

    BallPocketed,
    BallOffTable,
}

/// <summary>
/// One thing that happened during a shot, with the instant it happened. The rules engine reads
/// these instead of listening to physics signals — which is what removes the whole class of
/// "the turn never resolved" bugs: the timeline is complete and ordered by construction, so
/// there is no contact to miss (the old max_contacts_reported = 8 could silently drop rail hits)
/// and nothing to await.
/// </summary>
public readonly struct ShotEvent
{
    public readonly double Time;
    public readonly ShotEventType Type;
    public readonly int BallId;

    /// <summary>Second ball for BallHitBall, pocket index for BallPocketed, otherwise -1.</summary>
    public readonly int OtherId;

    public ShotEvent(double time, ShotEventType type, int ballId, int otherId = -1)
    {
        Time = time;
        Type = type;
        BallId = ballId;
        OtherId = otherId;
    }

    public override string ToString() => OtherId >= 0
        ? $"[{Time:F3}s] {Type} ball={BallId} other={OtherId}"
        : $"[{Time:F3}s] {Type} ball={BallId}";
}

/// <summary>
/// Sampled ball motion over the shot, for the presentation layer to play back. Stored flat
/// (frame-major) rather than as per-frame arrays so a long shot doesn't allocate thousands of
/// small objects.
/// </summary>
public sealed class ShotPlayback
{
    public readonly double FrameInterval;
    public readonly int BallCount;
    public readonly int FrameCount;

    private readonly Vec3d[] _positions;
    private readonly Vec3d[] _angularVelocities;
    private readonly bool[] _inPlay;

    internal ShotPlayback(
        double frameInterval,
        int ballCount,
        List<Vec3d> positions,
        List<Vec3d> angularVelocities,
        List<bool> inPlay)
    {
        FrameInterval = frameInterval;
        BallCount = ballCount;
        FrameCount = ballCount > 0 ? positions.Count / ballCount : 0;
        _positions = positions.ToArray();
        _angularVelocities = angularVelocities.ToArray();
        _inPlay = inPlay.ToArray();
    }

    public double Duration => FrameCount > 0 ? (FrameCount - 1) * FrameInterval : 0.0;

    public Vec3d PositionAt(int frame, int ball) => _positions[frame * BallCount + ball];
    public Vec3d AngularVelocityAt(int frame, int ball) => _angularVelocities[frame * BallCount + ball];
    public bool InPlayAt(int frame, int ball) => _inPlay[frame * BallCount + ball];
}

public sealed class ShotResult
{
    /// <summary>Ball states once everything has come to rest, indexed the same as the input.</summary>
    public readonly IReadOnlyList<BallState> FinalStates;

    public readonly IReadOnlyList<ShotEvent> Events;
    public readonly ShotPlayback Playback;

    /// <summary>Simulated duration in seconds — how long playback should take.</summary>
    public readonly double Duration;

    /// <summary>
    /// True when the shot hit the safety cap instead of settling naturally. Should never happen
    /// with sane inputs; if it does, the final states are still usable but something is wrong.
    /// </summary>
    public readonly bool TimedOut;

    internal ShotResult(
        IReadOnlyList<BallState> finalStates,
        IReadOnlyList<ShotEvent> events,
        ShotPlayback playback,
        double duration,
        bool timedOut)
    {
        FinalStates = finalStates;
        Events = events;
        Playback = playback;
        Duration = duration;
        TimedOut = timedOut;
    }

    /// <summary>First ball the cue ball touched, or -1 if it touched nothing (a foul in most rulesets).</summary>
    public int FirstBallContacted(int cueBallId)
    {
        foreach (var shotEvent in Events)
        {
            if (shotEvent.Type != ShotEventType.BallHitBall)
                continue;

            if (shotEvent.BallId == cueBallId)
                return shotEvent.OtherId;
            if (shotEvent.OtherId == cueBallId)
                return shotEvent.BallId;
        }

        return -1;
    }

    public bool AnyCushionContact()
    {
        foreach (var shotEvent in Events)
        {
            if (shotEvent.Type == ShotEventType.BallHitCushion)
                return true;
        }

        return false;
    }
}
