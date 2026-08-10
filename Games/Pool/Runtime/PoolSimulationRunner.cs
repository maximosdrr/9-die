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
    [Signal] public delegate void ShotStartedEventHandler();
    [Signal] public delegate void ShotFinishedEventHandler();

    public TableSpec Table { get; private set; }
    public bool IsPlaying => _playback != null;

    /// <summary>
    /// The shot that just played, complete with its ordered event timeline. This is what the rules
    /// read: the outcome is fully known the instant the ball is struck, so nothing has to wait for
    /// the table to settle and no contact can be missed.
    /// </summary>
    public ShotResult LastShot { get; private set; }

    /// <summary>Maps a simulation ball id back to its scene node.</summary>
    public Ball FindBall(int id) => _ballsById.TryGetValue(id, out var ball) ? ball : null;

    private ShotSimulator _simulator;
    private readonly System.Collections.Generic.List<Ball> _balls = new();
    private readonly System.Collections.Generic.Dictionary<int, Ball> _ballsById = new();

    private ShotPlayback _playback;
    private System.Collections.Generic.IReadOnlyList<ShotEvent> _events;
    private double _playbackTime;
    private int _nextEventIndex;
    private int[] _pendingFinalIds;
    private float[] _pendingFinalPositions;

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
        SetProcess(false);
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
        ball.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
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

        LastShot = result;
        BeginPlayback(result);

        EmitSignal(SignalName.ShotStarted);
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
        SetProcess(true);
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

        if (_playback == null && _drops.Count == 0)
            SetProcess(false);
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
        SetProcess(true);
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

        if (_pendingFinalIds != null && _pendingFinalPositions != null)
        {
            ApplyState(_pendingFinalIds, _pendingFinalPositions, preservePocketDrops: true);
            _pendingFinalIds = null;
            _pendingFinalPositions = null;
        }

        EmitSignal(SignalName.ShotFinished);
        if (_drops.Count == 0)
            SetProcess(false);
    }

    /// <summary>Stops presentation without emitting ShotFinished when the match is already over.</summary>
    public void CancelPlayback()
    {
        _playback = null;
        _events = null;
        _pendingFinalIds = null;
        _pendingFinalPositions = null;
        _playbackTime = 0.0;
        _nextEventIndex = 0;

        foreach (var ball in _balls)
            StopPresentationForBall(ball);

        _drops.Clear();
        SetProcess(false);
    }

    /// <summary>
    /// Server entry point for a shot. Rather than simulating alone and streaming ball positions,
    /// it broadcasts the authoritative pre-shot layout plus the shot itself. Every peer runs the
    /// same simulation for immediate playback and then applies the server's final correction.
    ///
    /// Sending the layout with each shot is what makes this safe without demanding bit-exact
    /// determinism across machines: floating point can differ slightly between runtimes, but the
    /// server's final state also prevents floating-point differences between runtimes from
    /// accumulating across shots. This replaces continuous position replication while the table
    /// is idle.
    /// </summary>
    public bool BroadcastShot(ShotInput shot)
    {
        if (!Multiplayer.IsServer())
            return false;

        CaptureState(out var ids, out var positions);
        var sanitized = shot.Sanitized();

        // The server computes the authoritative result directly. Remote peers receive the same
        // starting layout and input for immediate playback, followed by the final correction.
        if (!ExecuteShot(sanitized))
            return false;

        Rpc(MethodName.RpcPlayShot, ids, positions,
            (float)sanitized.AimYaw, (float)sanitized.Elevation, (float)sanitized.Speed,
            (float)sanitized.TipOffsetX, (float)sanitized.TipOffsetY);

        if (LastShot != null)
        {
            CaptureFinalState(LastShot, out var finalIds, out var finalPositions);
            Rpc(MethodName.RpcQueueFinalState, finalIds, finalPositions);
        }

        return true;
    }

    /// <summary>Pushes the authoritative layout to every peer without playing a shot, after a re-spot.</summary>
    public void BroadcastState()
    {
        if (!Multiplayer.IsServer())
            return;

        CaptureState(out var ids, out var positions);
        Rpc(MethodName.RpcSyncState, ids, positions);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcPlayShot(int[] ids, float[] positions,
        float aimYaw, float elevation, float speed, float tipOffsetX, float tipOffsetY)
    {
        ApplyState(ids, positions);
        ExecuteShot(new ShotInput(aimYaw, elevation, speed, tipOffsetX, tipOffsetY));
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcSyncState(int[] ids, float[] positions)
    {
        ApplyState(ids, positions);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RpcQueueFinalState(int[] ids, float[] positions)
    {
        if (_playback == null)
        {
            ApplyState(ids, positions, preservePocketDrops: true);
            return;
        }

        _pendingFinalIds = ids;
        _pendingFinalPositions = positions;
    }

    private static void CaptureFinalState(ShotResult result, out int[] ids, out float[] positions)
    {
        var live = new System.Collections.Generic.List<BallState>();
        foreach (var state in result.FinalStates)
        {
            if (state.InPlay)
                live.Add(state);
        }

        ids = new int[live.Count];
        positions = new float[live.Count * 2];
        for (var i = 0; i < live.Count; i++)
        {
            ids[i] = live[i].Id;
            positions[i * 2] = (float)live[i].Position.X;
            positions[i * 2 + 1] = (float)live[i].Position.Z;
        }
    }

    /// <summary>Snapshots every ball still in play as (id, x, z) on the cloth.</summary>
    public void CaptureState(out int[] ids, out float[] positions)
    {
        var live = new System.Collections.Generic.List<Ball>();
        foreach (var ball in _balls)
        {
            if (IsInstanceValid(ball) && ball.InPlay)
                live.Add(ball);
        }

        ids = new int[live.Count];
        positions = new float[live.Count * 2];

        for (var i = 0; i < live.Count; i++)
        {
            ids[i] = live[i].Index;
            positions[i * 2] = live[i].Position.X;
            positions[i * 2 + 1] = live[i].Position.Z;
        }
    }

    /// <summary>
    /// Restores a snapshot. Any ball missing from the list is treated as out of play, which is how
    /// a peer that somehow missed a pot catches up.
    /// </summary>
    public void ApplyState(int[] ids, float[] positions, bool preservePocketDrops = false)
    {
        var seen = new System.Collections.Generic.HashSet<int>();

        for (var i = 0; i < ids.Length; i++)
        {
            if (!_ballsById.TryGetValue(ids[i], out var ball) || !IsInstanceValid(ball))
                continue;

            seen.Add(ids[i]);
            ball.SetInPlay(true);
            ball.Position = new Vector3(
                positions[i * 2],
                (float)BilliardConstants.Radius,
                positions[i * 2 + 1]);
        }

        foreach (var ball in _balls)
        {
            if (!IsInstanceValid(ball) || seen.Contains(ball.Index))
                continue;

            ball.SetInPlay(false);
            if (!preservePocketDrops || !IsDropping(ball))
                ball.Visible = false;
        }

        if (!preservePocketDrops)
            _drops.Clear();
    }

    private bool IsDropping(Ball ball)
    {
        foreach (var drop in _drops)
        {
            if (drop.Ball == ball)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Stops any purely visual pocket/off-table fall before ball-in-hand takes ownership of the
    /// presentation. Without this, an old PocketDrop could move or hide the real ball after the
    /// server had already committed its new position.
    /// </summary>
    public void StopPresentationForBall(Ball ball)
    {
        for (var i = _drops.Count - 1; i >= 0; i--)
        {
            if (_drops[i].Ball == ball)
                _drops.RemoveAt(i);
        }

        if (IsInstanceValid(ball))
            ball.ApplyMotion(Vector3.Zero, Vector3.Zero, 0.0f);
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

    public Vector2 GlobalToTablePosition(Vector3 globalPosition)
    {
        var local = TableAnchor != null ? TableAnchor.ToLocal(globalPosition) : globalPosition;
        return new Vector2(local.X, local.Z);
    }

    public Vector3 TableToGlobalPosition(Vector2 tablePosition)
    {
        var local = new Vector3(tablePosition.X, (float)BilliardConstants.Radius, tablePosition.Y);
        return TableAnchor != null ? TableAnchor.ToGlobal(local) : local;
    }

    public Vector2 ClampToPlayableArea(Vector2 tablePosition)
    {
        var radius = (float)BilliardConstants.Radius;
        var centre = new Vector2((float)Table.PlayCentre.X, (float)Table.PlayCentre.Z);
        tablePosition.X = Mathf.Clamp(tablePosition.X,
            centre.X - (float)Table.HalfWidth + radius,
            centre.X + (float)Table.HalfWidth - radius);
        tablePosition.Y = Mathf.Clamp(tablePosition.Y,
            centre.Y - (float)Table.HalfLength + radius,
            centre.Y + (float)Table.HalfLength - radius);
        return tablePosition;
    }

    public bool TryValidatePlacement(Vector3 requestedGlobalPosition, Ball ignore, out Vector3 validatedGlobalPosition)
    {
        validatedGlobalPosition = Vector3.Zero;

        if (!float.IsFinite(requestedGlobalPosition.X) || !float.IsFinite(requestedGlobalPosition.Y)
            || !float.IsFinite(requestedGlobalPosition.Z) || Table == null || IsPlaying)
            return false;

        var tablePosition = GlobalToTablePosition(requestedGlobalPosition);
        if (!IsLegalTablePosition(tablePosition, ignore))
            return false;

        validatedGlobalPosition = TableToGlobalPosition(tablePosition);
        return true;
    }

    public bool TryFindNearestFreeSpot(Vector2 preferred, Ball ignore, out Vector2 result)
    {
        if (IsLegalTablePosition(preferred, ignore))
        {
            result = preferred;
            return true;
        }

        var step = (float)(2.0 * BilliardConstants.Radius) + 0.0001f;
        for (var i = 1; i <= 64; i++)
        {
            var towardFootRail = preferred + Vector2.Down * (step * i);
            if (IsLegalTablePosition(towardFootRail, ignore))
            {
                result = towardFootRail;
                return true;
            }

            var towardHeadRail = preferred + Vector2.Up * (step * i);
            if (IsLegalTablePosition(towardHeadRail, ignore))
            {
                result = towardHeadRail;
                return true;
            }
        }

        result = default;
        return false;
    }

    private bool IsLegalTablePosition(Vector2 tablePosition, Ball ignore)
    {
        var point = new Vec3d(tablePosition.X, BilliardConstants.Radius, tablePosition.Y);
        if (!Table.IsOverPlaySurface(point, -BilliardConstants.Radius))
            return false;

        var probe = new BallState(ignore?.Index ?? -1, point);
        return !BilliardCollisions.IsOverPocket(probe, Table, out _)
               && IsSpotFree(tablePosition, ignore);
    }
}
