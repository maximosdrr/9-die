namespace Pool.Simulation;

public enum BallMotion
{
    /// <summary>At rest, no spin. Terminal state.</summary>
    Stationary,

    /// <summary>The contact point is moving relative to the cloth — the ball is skidding.</summary>
    Sliding,

    /// <summary>Natural roll: the contact point is instantaneously at rest, v and ω are locked together.</summary>
    Rolling,

    /// <summary>Not translating, but still spinning about the vertical axis (leftover english).</summary>
    Spinning,

    /// <summary>Off the cloth, under gravity. Only reachable via a jump shot.</summary>
    Airborne,

    /// <summary>Fell in a pocket. Removed from play; no longer simulated.</summary>
    Pocketed,
}

/// <summary>
/// One ball's dynamic state. Mutable by design — the solver advances these in place across a
/// shot, and allocating fresh instances per substep would dominate the cost of the simulation.
/// Use <see cref="Clone"/> to take a snapshot.
///
/// Position is the ball CENTRE. Y is height above the cloth plane, so a resting ball sits at
/// Y = Radius, not 0.
/// </summary>
public sealed class BallState
{
    public int Id;
    public Vec3d Position;
    public Vec3d Velocity;
    public Vec3d AngularVelocity;
    public BallMotion Motion;

    public bool InPlay => Motion != BallMotion.Pocketed;
    public bool IsMoving => Motion != BallMotion.Stationary && Motion != BallMotion.Pocketed;

    public BallState(int id, Vec3d position)
    {
        Id = id;
        Position = position;
        Velocity = Vec3d.Zero;
        AngularVelocity = Vec3d.Zero;
        Motion = BallMotion.Stationary;
    }

    /// <summary>
    /// Relative velocity of the ball's contact point against the cloth: u = v + R * (k̂ × ω).
    /// This is the quantity sliding friction acts on, and natural roll is exactly u == 0.
    /// Meaningless while airborne (there is no contact point).
    /// </summary>
    public Vec3d ContactPointVelocity()
    {
        var spinContribution = Vec3d.Up.Cross(AngularVelocity) * BilliardConstants.Radius;
        return Velocity.Flat + spinContribution.Flat;
    }

    public BallState Clone()
    {
        return new BallState(Id, Position)
        {
            Velocity = Velocity,
            AngularVelocity = AngularVelocity,
            Motion = Motion,
        };
    }

    public void CopyFrom(BallState other)
    {
        Position = other.Position;
        Velocity = other.Velocity;
        AngularVelocity = other.AngularVelocity;
        Motion = other.Motion;
    }
}
