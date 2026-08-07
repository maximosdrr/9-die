using Godot;
using Godot.Collections;
using Pool.Simulation;

/// <summary>
/// Owns the analytic billiard simulation and drives the Ball nodes from its output.
///
/// The whole shot is computed the instant it is struck, then played back. That inversion is what
/// makes the rest of the game simpler: the outcome is known before the first frame of animation,
/// so the rules never have to wait for the table to settle and can never miss a contact. It is
/// also what makes the shot reproducible from ShotInput alone, which the networking step builds on.
///
/// Ball nodes are children of the table anchor, and simulation coordinates ARE their local
/// coordinates: origin at the centre of the cloth, +Y up, so a resting ball sits at y = Radius.
/// </summary>
[GlobalClass]
public partial class PoolSimulationRunner : Node
{
    /// <summary>
    /// Container the ball nodes live under. Simulation coordinates are this node's local
    /// coordinates, so at setup it is snapped onto the table's PoolTableGeometry — which is what
    /// lets a differently sized table drop in without touching anything here.
    /// </summary>
    [Export] public Node3D TableAnchor;

    [ExportGroup("Pocket drop")]
    /// <summary>How far below the cloth a potted ball falls before it stops being drawn.</summary>
    [Export] public float PocketDropDistance = 0.3f;

    /// <summary>
    /// How quickly a falling ball is drawn toward the centre of the pocket. High enough that it
    /// clearly goes into the hole rather than through the rail, low enough that it still reads as
    /// the ball catching the jaw and dropping.
    /// </summary>
    [Export] public float PocketSteerRate = 12.0f;

    [Export] public float DropGravity = 9.81f;

    [Signal] public delegate void BallPocketedEventHandler(Ball ball);
    [Signal] public delegate void BallDrivenOffTableEventHandler(Ball ball);
    [Signal] public delegate void ShotFinishedEventHandler();

    public TableSpec Table { get; private set; }
    public bool IsPlaying => _playback != null;

    private ShotSimulator _simulator;
    private readonly System.Collections.Generic.List<Ball> _balls = new();
    private readonly System.Collections.Generic.Dictionary<int, Ball> _ballsById = new();

    private ShotPlayback _playback;
    private System.Collections.Generic.IReadOnlyList<ShotEvent> _events;
    private double _playbackTime;
    private int _nextEventIndex;

    /// <summary>
    /// A ball on its way down a pocket. This is purely presentational and runs outside the
    /// simulation: the shot's outcome was decided the moment the ball was captured, so the drop
    /// must not hold up the turn, and several balls can be falling at once while play continues.
    /// </summary>
    private sealed class PocketDrop
    {
        public Ball Ball;
        public Vector2 Target;
        public Vector3 Spin;
        public float VerticalSpeed;
        public float StartHeight;
    }

    private readonly System.Collections.Generic.List<PocketDrop> _drops = new();

    public override void _Ready()
    {
        UseGeometry(null);
    }

    /// <summary>
    /// Adopts a table's geometry. Passing null falls back to a symmetric default so the runner is
    /// always usable — tests construct it standalone.
    /// </summary>
    public void UseGeometry(PoolTableGeometry geometry)
    {
        Table = geometry != null ? geometry.BuildSpec() : TableSpec.CreateDefault();
        _simulator = new ShotSimulator(Table);

        // Line the ball container up with the cloth centre the geometry defines, so simulation
        // coordinates and ball local coordinates are the same thing.
        if (geometry != null && TableAnchor != null && TableAnchor.IsInsideTree())
            TableAnchor.GlobalTransform = geometry.GlobalTransform;
    }

    /// <summary>
    /// Registers the balls this runner drives. Ball.Index is the game-facing id (0 is the cue
    /// ball), and the simulation reuses it so events map straight back to nodes.
    /// </summary>
    public void Setup(Ball cueBall, Array<Ball> objectBalls)
    {
        _balls.Clear();
        _ballsById.Clear();
        _drops.Clear();

        if (cueBall != null)
            Register(cueBall);

        foreach (var ball in objectBalls)
        {
            if (ball != null && ball != cueBall)
                Register(ball);
        }
    }

    private void Register(Ball ball)
    {
        _balls.Add(ball);
        _ballsById[ball.Index] = ball;
    }

    /// <summary>
    /// Runs a shot to completion and begins playing it back. Returns false when the shot was
    /// rejected outright, so the caller can leave the turn untouched — the old code emitted its
    /// "struck" signal before validating the force, which let a zero-power shot start a turn that
    /// could never end.
    /// </summary>
    public bool ExecuteShot(ShotInput shot)
    {
        var sanitized = shot.Sanitized();
        if (sanitized.Speed <= 0.0 || _ballsById.Count == 0)
            return false;

        if (!_ballsById.TryGetValue(0, out var cueBall) || !IsInstanceValid(cueBall))
            return false;

        var initial = new System.Collections.Generic.List<BallState>(_balls.Count);
        foreach (var ball in _balls)
            initial.Add(ReadState(ball));

        var result = _simulator.Simulate(initial, cueBallId: 0, sanitized);
        if (result.TimedOut)
            GD.PushWarning("[PoolSimulation] A tacada atingiu o limite de tempo sem assentar.");

        BeginPlayback(result);
        cueBall.NotifyStruck();

        foreach (var ball in _balls)
            ball.NotifyStartedMoving();

        return true;
    }

    private BallState ReadState(Ball ball)
    {
        var local = ball.Position;
        var state = new BallState(ball.Index, new Vec3d(local.X, BilliardConstants.Radius, local.Z));
        if (!ball.InPlay)
            state.Motion = BallMotion.Pocketed;
        return state;
    }

    private void BeginPlayback(ShotResult result)
    {
        _playback = result.Playback;
        _events = result.Events;
        _playbackTime = 0.0;
        _nextEventIndex = 0;
    }

    public override void _Process(double delta)
    {
        // Drops keep running after the shot itself is over, so this sits outside the playback guard.
        UpdateDrops(delta);

        if (_playback == null)
            return;

        _playbackTime += delta;

        FireDueEvents();
        ApplyPlaybackFrame(delta);

        if (_playbackTime >= _playback.Duration)
            FinishPlayback();
    }

    private void FireDueEvents()
    {
        while (_nextEventIndex < _events.Count && _events[_nextEventIndex].Time <= _playbackTime)
        {
            DispatchEvent(_events[_nextEventIndex]);
            _nextEventIndex++;
        }
    }

    private void DispatchEvent(ShotEvent shotEvent)
    {
        if (!_ballsById.TryGetValue(shotEvent.BallId, out var ball) || !IsInstanceValid(ball))
            return;

        switch (shotEvent.Type)
        {
            case ShotEventType.BallHitBall:
                if (_ballsById.TryGetValue(shotEvent.OtherId, out var other) && IsInstanceValid(other))
                {
                    ball.NotifyBallContacted(other);
                    other.NotifyBallContacted(ball);
                }
                break;

            case ShotEventType.BallHitCushion:
                ball.NotifyTouchedRail();
                break;

            case ShotEventType.BallHitCloth:
                ball.NotifyBouncedOnCloth();
                break;

            case ShotEventType.BallPocketed:
                ball.SetInPlay(false);
                BeginPocketDrop(ball, shotEvent.OtherId);
                EmitSignal(SignalName.BallPocketed, ball);
                break;

            case ShotEventType.BallOffTable:
                ball.SetInPlay(false);
                BeginPocketDrop(ball, -1);
                EmitSignal(SignalName.BallDrivenOffTable, ball);
                break;
        }
    }

    /// <summary>
    /// Starts the fall for a ball that has just been captured. A pocket index of -1 means it was
    /// driven off the table instead, in which case it just drops straight down from where it is.
    /// </summary>
    private void BeginPocketDrop(Ball ball, int pocketIndex)
    {
        var target = new Vector2(ball.Position.X, ball.Position.Z);
        if (pocketIndex >= 0 && pocketIndex < Table.Pockets.Count)
        {
            var centre = Table.Pockets[pocketIndex].Center;
            target = new Vector2((float)centre.X, (float)centre.Z);
        }

        _drops.Add(new PocketDrop
        {
            Ball = ball,
            Target = target,
            // Carry the spin it had on the way in so it keeps tumbling as it falls.
            Spin = ball.AngularVelocitySnapshot,
            VerticalSpeed = 0.0f,
            StartHeight = ball.Position.Y,
        });
    }

    private void UpdateDrops(double delta)
    {
        var step = (float)delta;

        for (var i = _drops.Count - 1; i >= 0; i--)
        {
            var drop = _drops[i];

            if (!IsInstanceValid(drop.Ball))
            {
                _drops.RemoveAt(i);
                continue;
            }

            drop.VerticalSpeed -= DropGravity * step;

            var position = drop.Ball.Position;

            // Exponential pull toward the pocket centre, written so the rate is the same at any
            // frame rate rather than drifting with it.
            var blend = 1.0f - Mathf.Exp(-PocketSteerRate * step);
            var horizontal = new Vector2(position.X, position.Z).Lerp(drop.Target, blend);

            position.X = horizontal.X;
            position.Z = horizontal.Y;
            position.Y += drop.VerticalSpeed * step;
            drop.Ball.Position = position;

            drop.Ball.ApplyMotion(Vector3.Zero, drop.Spin, step);

            if (drop.StartHeight - position.Y < PocketDropDistance)
                continue;

            drop.Ball.Visible = false;
            _drops.RemoveAt(i);
        }
    }

    private void ApplyPlaybackFrame(double delta)
    {
        var exactFrame = _playbackTime / _playback.FrameInterval;
        var frame = (int)exactFrame;
        if (frame >= _playback.FrameCount - 1)
            frame = _playback.FrameCount - 1;

        var blend = exactFrame - frame;
        var nextFrame = Mathf.Min(frame + 1, _playback.FrameCount - 1);

        for (var i = 0; i < _balls.Count; i++)
        {
            var ball = _balls[i];
            if (!IsInstanceValid(ball) || !_playback.InPlayAt(frame, i))
                continue;

            var from = _playback.PositionAt(frame, i);
            var to = _playback.PositionAt(nextFrame, i);
            var position = from + (to - from) * blend;

            ball.Position = new Vector3((float)position.X, (float)position.Y, (float)position.Z);

            // Linear velocity is recovered from the sampled track rather than stored separately;
            // the impact sounds are the only consumer and a frame's resolution is plenty for them.
            var velocity = (to - from) / _playback.FrameInterval;
            var spin = _playback.AngularVelocityAt(frame, i);

            ball.ApplyMotion(
                new Vector3((float)velocity.X, (float)velocity.Y, (float)velocity.Z),
                new Vector3((float)spin.X, (float)spin.Y, (float)spin.Z),
                (float)delta);
        }
    }

    private void FinishPlayback()
    {
        _playback = null;
        _events = null;

        foreach (var ball in _balls)
        {
            if (IsInstanceValid(ball))
                ball.NotifyStoppedMoving();
        }

        EmitSignal(SignalName.ShotFinished);
    }

    /// <summary>Places a ball at a table-local spot, used by respawn and ball-in-hand.</summary>
    public void PlaceBall(Ball ball, Vector2 tablePosition)
    {
        ball.Position = new Vector3(tablePosition.X, (float)BilliardConstants.Radius, tablePosition.Y);
        ball.SetInPlay(true);
    }

    /// <summary>
    /// True when the spot is clear of every other ball in play — ball-in-hand needs this, and
    /// so does re-spotting, since two balls sharing a position is no longer prevented by a
    /// physics solver pushing them apart.
    /// </summary>
    public bool IsSpotFree(Vector2 tablePosition, Ball ignore)
    {
        var minimumDistance = (float)(2.0 * BilliardConstants.Radius);

        foreach (var ball in _balls)
        {
            if (ball == ignore || !IsInstanceValid(ball) || !ball.InPlay)
                continue;

            var offset = new Vector2(ball.Position.X, ball.Position.Z) - tablePosition;
            if (offset.Length() < minimumDistance)
                return false;
        }

        return true;
    }
}
