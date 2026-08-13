using Godot;
using Godot.Collections;

/// <summary>
/// A player sat at a table for the length of a match: owns the chair, the two camera views and the
/// hold-to-leave. It knows nothing about what is being played.
///
/// Two views, both driven from here rather than from a fixed marker in the table scene, so each
/// player looks around independently and sees the table from their own side:
///   - the SEAT view, a first-person head that turns with the mouse within a neck's range;
///   - the TOP view, straight down over the surface, rotated so their own side is nearest.
///
/// A subclass supplies four things and keeps everything else: which view it drives
/// (<see cref="HandView"/>), what the overhead camera is centred on (<see cref="TableSurface"/>),
/// where a given player sits (<see cref="SeatFor"/>), and how to repaint (<see cref="RefreshView"/>).
/// </summary>
[GlobalClass]
public partial class SeatedTableController : GameController
{
    /// <summary>
    /// Swap this to change the entire hand presentation — see <see cref="SeatedHandView"/> for the
    /// contract it must keep.
    /// </summary>
    [Export] public PackedScene HandViewScene;

    [ExportGroup("Seat view")]
    /// <summary>
    /// Usually tighter than the walking camera so what is on the table stays readable from the chair
    /// without changing physical sizes or any layout shared by every peer.
    /// </summary>
    [Export] public float SeatFov = 55.0f;

    /// <summary>
    /// Fine adjustment from the anatomical eye marker, in the seat's local axes. It normally stays
    /// at zero; the marker itself is measured from the seated character's eye bones.
    /// </summary>
    [Export] public Vector3 SeatViewOffset = Vector3.Zero;

    [Export] public float MouseSensitivity = 0.004f;

    /// <summary>How far the head turns to either side before a real person would move their body.</summary>
    [Export] public float MaxYawDeg = 100.0f;

    [Export] public float MinPitchDeg = -65.0f;
    [Export] public float MaxPitchDeg = 25.0f;

    /// <summary>Where the head rests: tilted down at the table, which is what the player wants to see.</summary>
    [Export] public float RestPitchDeg = -35.0f;

    /// <summary>
    /// Animation requested while seated. PlayerStrike falls back to Idle until this clip is added to
    /// the character, so adding the future animation needs no controller rewrite.
    /// </summary>
    [Export] public string SeatedAnimationName = CharacterVisual.Clips.Sit;

    /// <summary>Optional transition between sitting down and the seated loop.</summary>
    [Export] public string SeatedPreparationAnimationName = "";

    /// <summary>Loop queued after the sit transition.</summary>
    [Export] public string SeatedIdleAnimationName = CharacterVisual.Clips.IdleSit;

    protected virtual string SeatedPreparationClip => SeatedPreparationAnimationName;
    protected virtual string SeatedIdleClip => SeatedIdleAnimationName;

    [ExportGroup("Top view")]
    [Export] public float TopFov = 52.0f;

    /// <summary>Height above the surface. Framed so the whole playing area fills the shot at TopFov.</summary>
    [Export] public float TopHeight = 0.88f;

    /// <summary>
    /// How far the overhead view slides per pixel of mouse movement. Without panning, a crosshair
    /// locked to the screen centre would only ever point at the middle of the table.
    /// </summary>
    [Export] public float TopPanSensitivity = 0.0012f;

    /// <summary>How far the overhead view may wander from the middle of the surface.</summary>
    [Export] public Vector2 TopPanLimit = new(0.34f, 0.34f);

    [ExportGroup("Leaving")]
    /// <summary>Held, not tapped: getting up mid-match forfeits, so it must not be a slip.</summary>
    [Export] public float LeaveHoldSeconds = 1.0f;

    [ExportGroup("Seat arrival")]
    /// <summary>World-space speed of the short, collision-free approach to the assigned chair.</summary>
    [Export] public float SeatApproachSpeed = 1.8f;
    [Export] public float MinimumSeatApproachSeconds = 0.25f;
    [Export] public float MaximumSeatApproachSeconds = 2.5f;
    [Export] public float SeatArrivalTolerance = 0.025f;

    internal const float MaximumAllowedSeatApproachSeconds = 5.0f;

    public Player Player;
    public GlobalCamera Camera;

    /// <summary>The match this seat belongs to. Subclasses read their own type off this.</summary>
    protected TableGame Table;

    private const string InputTopView = "toggle_top_view";
    private const string InputLeave = "leave_table";

    [ExportGroup("Scene References")]
    [Export] public Node3D LookRig;
    [Export] public Node3D LookPitch;
    [Export] public RemoteTransform3D RemoteSeat;
    [Export] public Node3D TopRig;
    [Export] public RemoteTransform3D RemoteTop;

    private float _seatYaw;
    private float _lookYaw;
    private float _lookPitch2;
    private bool _inTopView;
    private bool _topViewActive;
    private Vector2 _topPan;
    private Vector3 _surfaceCentre;
    private float _leaveHeld;
    private Vector3 _cachedStandPosition;
    private float _cachedStandYaw;
    private bool _hasCachedStandExit;
    private bool _waitingForSeatArrival;
    private Vector3 _seatTargetPosition;
    private float _seatTargetYaw;

    /// <summary>Whether the player is currently in their chair rather than walking.</summary>
    protected bool Seated { get; private set; }

    protected bool InTopView => _inTopView;

    // ---------------------------------------------------------------- what a subclass supplies

    /// <summary>The view being driven. Null until the owning peer spawns one.</summary>
    protected virtual SeatedHandView HandView => null;

    /// <summary>The playing surface the overhead view centres on.</summary>
    protected virtual Node3D TableSurface => null;

    /// <summary>This player's chair, or null if they have none.</summary>
    protected virtual Marker3D SeatFor(string playerId) => null;

    /// <summary>Root holding the seat markers, consulted for the default stand-up distance.</summary>
    protected virtual Node3D SeatsRoot => null;

    /// <summary>Repaint the view from current state.</summary>
    protected virtual void RefreshView() { }

    /// <summary>
    /// Wire up game-specific signals. Returns false to abort setup — the wrong kind of game was
    /// plugged in, and seating the player would only hide the mistake.
    /// </summary>
    protected virtual bool OnSetup() => true;

    /// <summary>The view has been spawned and added; connect its game-specific intents here.</summary>
    protected virtual void OnHandViewSpawned(SeatedHandView view) { }

    // ---------------------------------------------------------------- lifecycle

    public override void _Ready()
    {
        // Both rigs are placed in world space from the seat, so they must not inherit the player's
        // transform — the same trick AimCameraPivot uses to ride the cue ball.
        LookRig.TopLevel = true;
        TopRig.TopLevel = true;

        SetProcessUnhandledInput(false);
        SetProcess(false);
    }

    public override void Setup(Player parent, TableGame tableGame, GlobalCamera camera)
    {
        Player = parent;
        Table = tableGame;
        Camera = camera;

        if (!OnSetup())
            return;

        // Every peer owns a physical copy of every player. Disable the seated body's simulation and
        // collider on all of them, not only on its authority, or a spectator could still collide
        // with an apparently motionless remote player occupying the same chair.
        var assignedSeat = SeatFor((string)Player.Name);
        if (assignedSeat != null)
        {
            // Cache this on every physical copy, not only on the input authority. Permanent
            // removal releases occupancy before mode handlers run, but the server still needs this
            // trusted marker to move the old body clear before restoring its collider.
            CacheStandExit(assignedSeat);
            PrepareSeatArrival(assignedSeat);
            if (Multiplayer.IsServer()
                && TryGetAuthoritativePose(seated: true, out var position, out var yaw))
            {
                Player.ApplyServerMovementMode(
                    walking: false, position, yaw, SeatApproachDurationTo(position));
            }
        }

        if (!IsMultiplayerAuthority())
            return;

        SpawnHandView();
        CanTakeControl = Table?.IsMatchActive == true;
        TakeControl();
        RefreshView();
    }

    public override void _ExitTree()
    {
        if ((Seated || _waitingForSeatArrival) && IsMultiplayerAuthority())
            MoveToStandExit();
        else if (Multiplayer.IsServer() && IsInstanceValid(Player)
            && TryGetAuthoritativePose(seated: false, out var position, out var yaw))
            Player.ApplyServerMovementMode(walking: true, position, yaw);

        // GiveControl resets the FOV, but a controller that is freed rather than handed back never
        // runs it — and the walking camera would stay at the seated FOV for the rest of the session.
        if (Seated && IsMultiplayerAuthority())
            Camera?.ResetFov();

        if (IsInstanceValid(Player))
        {
            Player.CancelMovementModeTransition();
            Player.ExitSeatedGameMode();
        }
    }

    public override void _Process(double delta)
    {
        if (_waitingForSeatArrival)
            TryFinishSeatArrival();

        if (!Seated || !IsMultiplayerAuthority())
            return;

        // Leaving the MATCH uses Q and is a hold rather than a press, so it cannot be confused with
        // the harmless E toggle that only gets up from the chair.
        if (Input.IsActionPressed(InputLeave))
        {
            _leaveHeld += (float)delta;

            if (_leaveHeld >= LeaveHoldSeconds)
            {
                _leaveHeld = 0.0f;
                HandView?.ShowNotice("Saindo da mesa", 2.0f);
                RequestSurrender();
                return;
            }

            var remaining = Mathf.Max(0.0f, LeaveHoldSeconds - _leaveHeld);
            HandView?.ShowNotice($"Segure para sair da mesa… {remaining:F1}s", 0.2f);
            return;
        }

        _leaveHeld = 0.0f;
    }

    private void SpawnHandView()
    {
        if (HandViewScene == null)
        {
            GD.PushError($"{GetType().Name} sem HandViewScene: o jogador não terá como jogar.");
            return;
        }

        var view = HandViewScene.Instantiate<SeatedHandView>();
        view.Name = "HandView";

        // Authority has to be set before the view enters the tree, because its _Ready hides itself
        // on peers that do not own it.
        view.SetMultiplayerAuthority(Player.Id);
        AddChild(view);

        OnHandViewSpawned(view);
        view.SurrenderRequested += RequestSurrender;
    }

    protected void RequestSurrender() => Table?.RequestSurrender((string)Player.Name);

    // ---------------------------------------------------------------- seating and views

    /// <summary>Returns from free walking to this player's chair without depending on turn order.</summary>
    public override void TakeControl()
    {
        if (!IsMultiplayerAuthority() || !IsInstanceValid(Table) || !Table.IsMatchActive
            || !IsInstanceValid(Player))
            return;

        if (SeatFor((string)Player.Name) == null)
        {
            GD.PushWarning($"Sem assento para o jogador {Player.Name}; não foi possível voltar à mesa.");
            return;
        }

        Player.EnterSeatedGameMode();
        TakeSeat();
        CanTakeControl = true;
        RefreshView();
    }

    /// <summary>
    /// Starts the short approach and builds both camera rigs around the server-assigned seat. The
    /// body does not begin the sit clip until it has actually reached that marker.
    /// </summary>
    private void TakeSeat()
    {
        // Every view change below is null-guarded, so without this the player is seated and then
        // left staring wherever they were facing, with no clue why. Loud, because the cause is a
        // wiring mistake in the level rather than anything the player did.
        if (Camera == null)
        {
            GD.PushError($"Mesa sem câmera: o jogador {Player.Name} vai sentar mas a visão não vai "
                         + "mudar. Main precisa chamar SetCamera nesta mesa.");
        }

        var seat = SeatFor((string)Player.Name);
        if (seat == null)
        {
            GD.PushWarning($"Sem assento para o jogador {Player.Name}; ele fica em pé onde estava.");
            return;
        }

        // PlayerGameHandler hides every controller it equips, which is right for the pool cue but
        // would leave the camera rigs inside a hidden subtree. Nothing here is visible anyway.
        Show();

        PrepareSeatArrival(seat);
        Player.RequestAuthoritativeMovementMode(walking: false);

        Player.GiveControl();

        // The eye point comes from the seat's own marker so an artist can raise or lower it per
        // chair without touching code.
        var eye = seat.GetNodeOrNull<Node3D>("SeatView");
        var eyeTransform = eye?.GlobalTransform ?? seat.GlobalTransform;
        LookRig.GlobalPosition = eyeTransform.Origin
                                 + eyeTransform.Basis.Orthonormalized() * SeatViewOffset;
        CacheStandExit(seat);

        _lookYaw = 0.0f;
        _lookPitch2 = Mathf.DegToRad(RestPitchDeg);
        ApplyLookRotation();

        _surfaceCentre = TableSurface != null
            ? TableSurface.GlobalPosition
            : LookRig.GlobalPosition;
        _topPan = Vector2.Zero;

        PlaceTopRig();

        _inTopView = false;
        _leaveHeld = 0.0f;
        TryFinishSeatArrival();
    }

    private void PrepareSeatArrival(Marker3D seat)
    {
        _seatTargetPosition = seat.GlobalPosition;
        _seatTargetYaw = seat.GlobalRotation.Y;
        _seatYaw = _seatTargetYaw;
        _waitingForSeatArrival = true;
        Seated = false;

        Player.EnterSeatedGameMode();
        Player.Velocity = Vector3.Zero;
        if (Player.GlobalPosition.DistanceTo(_seatTargetPosition) > SeatArrivalTolerance)
            Player.CharacterVisual?.Play(CharacterVisual.Clips.Walk, 0.12);

        // Remote copies normally do no controller work. They process only while approaching so the
        // same server marker, not the place where their owner pressed F, controls their final pose.
        SetProcess(true);
    }

    internal float SeatApproachDurationTo(Vector3 targetPosition)
    {
        var distance = Player == null ? 0.0f : Player.GlobalPosition.DistanceTo(targetPosition);
        if (distance <= Mathf.Max(0.001f, SeatArrivalTolerance))
            return 0.0f;

        var seconds = distance / Mathf.Max(0.1f, SeatApproachSpeed);
        return Mathf.Clamp(seconds,
            Mathf.Max(0.0f, MinimumSeatApproachSeconds),
            Mathf.Min(MaximumAllowedSeatApproachSeconds,
                Mathf.Max(MinimumSeatApproachSeconds, MaximumSeatApproachSeconds)));
    }

    private void TryFinishSeatArrival()
    {
        if (!_waitingForSeatArrival
            || Player.MovementModeTransitionActive
            || Player.GlobalPosition.DistanceTo(_seatTargetPosition) > SeatArrivalTolerance
            || Mathf.Abs(Mathf.AngleDifference(Player.GlobalRotation.Y, _seatTargetYaw)) > 0.02f)
        {
            return;
        }

        _waitingForSeatArrival = false;
        Seated = true;
        Player.GlobalPosition = _seatTargetPosition;
        Player.GlobalRotation = new Vector3(0.0f, _seatTargetYaw, 0.0f);
        Player.Velocity = Vector3.Zero;
        Player.ResetPhysicsInterpolation();

        if (!IsMultiplayerAuthority())
        {
            SetProcess(false);
            return;
        }

        Player.EnterSeatedAnimation(
            SeatedAnimationName, SeatedPreparationClip, SeatedIdleClip);
        HandView?.SetInteractive(true);
        SetProcessUnhandledInput(true);
        SetProcess(true);
        ShowSeatView();

        // Captured for the whole match: nothing is clicked any more, the hand is driven from the
        // keyboard and the aim is the centre of the screen, so looking around is just moving the
        // mouse. Escape gives the cursor back when the player needs to leave the window.
        InputFocus.Capture();
    }

    /// <summary>
    /// Parks the overhead rig above the middle of the surface, turned so the player's own side of
    /// the table is at the bottom of their screen. Each player gets their own orientation because
    /// the rig lives on the controller rather than in the shared table scene.
    /// </summary>
    private void PlaceTopRig()
    {
        // The pan is applied in the player's own frame, so pushing the mouse right always slides
        // the view right from where they are sitting, whichever side of the table that is.
        var offset = new Vector3(_topPan.X, TopHeight, _topPan.Y).Rotated(Vector3.Up, _seatYaw);

        TopRig.GlobalPosition = _surfaceCentre + offset;
        TopRig.GlobalRotation = new Vector3(-Mathf.Pi * 0.5f, _seatYaw, 0.0f);
    }

    private void ApplyLookRotation()
    {
        LookRig.GlobalRotation = new Vector3(0.0f, _seatYaw + _lookYaw, 0.0f);
        LookPitch.Rotation = new Vector3(_lookPitch2, 0.0f, 0.0f);
        Player?.SetCameraLook(_lookYaw, _lookPitch2);
    }

    private void ShowSeatView()
    {
        Camera?.SetGlobalCameraFov(SeatFov);
        Camera?.TransitionTo(RemoteSeat);
    }

    private void ShowTopView()
    {
        PlaceTopRig();
        Camera?.SetGlobalCameraFov(TopFov);
        Camera?.TransitionTo(RemoteTop);
    }

    public void ToggleTopView()
    {
        if (!Seated || !IsMultiplayerAuthority())
            return;

        _inTopView = !_inTopView;
        _topPan = Vector2.Zero;

        if (_inTopView)
        {
            Player?.ResetCameraLook();
            ShowTopView();
        }
        else
        {
            ApplyLookRotation();
            ShowSeatView();
        }

        HandView?.SetTopViewActive(_inTopView);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Seated || !IsMultiplayerAuthority())
            return;

        if (@event.IsActionPressed(InputTopView))
        {
            ToggleTopView();
            GetViewport().SetInputAsHandled();
            return;
        }

        // Escape hands the cursor back so the player can leave the window; any click takes it
        // again. HeadPivot normally owns this, but its input is off while seated.
        if (@event.IsActionPressed("ui_cancel"))
        {
            InputFocus.Release();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true } && !InputFocus.IsCaptured)
        {
            InputFocus.Capture();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventMouseMotion motion || !InputFocus.IsCaptured)
            return;

        var screenDelta = PointerMotion.ReadScreenDelta(motion);
        if (screenDelta.IsZeroApprox())
            return;

        if (_inTopView)
            PanTopView(screenDelta);
        else
            ApplyLook(screenDelta);

        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Slides the overhead view across the surface. The crosshair never leaves the middle of the
    /// screen, so moving the camera is how the player reaches the far side of the table from above.
    /// </summary>
    private void PanTopView(Vector2 relative)
    {
        _topPan = new Vector2(
            Mathf.Clamp(_topPan.X + relative.X * TopPanSensitivity, -TopPanLimit.X, TopPanLimit.X),
            Mathf.Clamp(_topPan.Y + relative.Y * TopPanSensitivity, -TopPanLimit.Y, TopPanLimit.Y));

        PlaceTopRig();
    }

    /// <summary>
    /// Turns the head. Yaw is clamped either side of the seat's facing rather than wrapping, so
    /// the player can look around the bar but never ends up facing backwards while seated.
    /// </summary>
    private void ApplyLook(Vector2 relative)
    {
        var yawLimit = Mathf.DegToRad(MaxYawDeg);

        _lookYaw = Mathf.Clamp(_lookYaw - relative.X * MouseSensitivity, -yawLimit, yawLimit);
        _lookPitch2 = Mathf.Clamp(
            _lookPitch2 - relative.Y * MouseSensitivity,
            Mathf.DegToRad(MinPitchDeg),
            Mathf.DegToRad(MaxPitchDeg));

        ApplyLookRotation();
    }

    // ---------------------------------------------------------------- control handover

    public override void GiveControl()
    {
        if (!IsInstanceValid(Player))
            return;

        if (!IsMultiplayerAuthority())
        {
            _waitingForSeatArrival = false;
            Seated = false;
            SetProcess(false);
            Player.CancelMovementModeTransition();
            if (Multiplayer.IsServer()
                && TryGetAuthoritativePose(seated: false, out var position, out var yaw))
            {
                Player.ApplyServerMovementMode(walking: true, position, yaw);
            }
            Player.ExitSeatedGameMode();
            return;
        }

        if (Seated || _waitingForSeatArrival)
            MoveToStandExit();

        Seated = false;
        _waitingForSeatArrival = false;
        SetProcessUnhandledInput(false);
        SetProcess(false);

        HandView?.SetInteractive(false);
        Camera?.ResetFov();
        Player.ResetCameraLook();
        Player.ExitGameControllerMode();
        Player.ExitSeatedGameMode();
    }

    /// <summary>
    /// Moves the character clear of the chair before restoring its collider. StandExit is a visible
    /// marker under each seat, so its final placement can be tuned directly in the editor.
    /// </summary>
    private void MoveToStandExit()
    {
        var seat = SeatFor((string)Player.Name);
        if (seat != null && seat.IsInsideTree())
            CacheStandExit(seat);
        if (!_hasCachedStandExit)
            return;

        Player.CancelMovementModeTransition();
        Player.GlobalPosition = _cachedStandPosition;
        Player.GlobalRotation = new Vector3(0.0f, _cachedStandYaw, 0.0f);
        Player.Velocity = Vector3.Zero;
        Player.ResetPhysicsInterpolation();
        Player.RequestAuthoritativeMovementMode(walking: true);
    }

    /// <summary>
    /// Resolves a mode change exclusively from server-owned scene markers and match membership.
    /// The client never supplies a coordinate, so the reliable seating RPC cannot be repurposed as
    /// a teleport primitive.
    /// </summary>
    internal bool TryGetAuthoritativePose(bool seated, out Vector3 position, out float yaw)
    {
        position = Vector3.Zero;
        yaw = 0.0f;

        if (!IsInstanceValid(Player) || !IsInstanceValid(Table))
            return false;

        if (seated)
        {
            var playerId = (string)Player.Name;
            if (!Table.IsMatchActive || !Table.TurnOrder.Contains(playerId))
                return false;

            var assignedSeat = SeatFor(playerId);
            if (assignedSeat == null || !assignedSeat.IsInsideTree())
                return false;

            position = assignedSeat.GlobalPosition;
            yaw = assignedSeat.GlobalRotation.Y;
            return true;
        }

        if (_hasCachedStandExit)
        {
            position = _cachedStandPosition;
            yaw = _cachedStandYaw;
            return true;
        }

        var seat = SeatFor((string)Player.Name);
        if (seat == null || !seat.IsInsideTree())
            return false;

        var standExit = seat.GetNodeOrNull<Marker3D>("StandExit");
        position = standExit?.GlobalPosition
            ?? seat.ToGlobal(new Vector3(
                0.0f, 0.0f, (SeatsRoot as TableSeatAnchors)?.StandBackDistance ?? 0.55f));
        yaw = standExit?.GlobalRotation.Y ?? seat.GlobalRotation.Y;
        return true;
    }

    internal void CacheAssignedStandExit()
    {
        if (!IsInstanceValid(Player))
            return;

        CacheStandExit(SeatFor((string)Player.Name));
    }

    private void CacheStandExit(Marker3D seat)
    {
        if (seat == null || !seat.IsInsideTree())
            return;
        var standExit = seat.GetNodeOrNull<Marker3D>("StandExit");
        _cachedStandPosition = standExit?.GlobalPosition
            ?? seat.ToGlobal(new Vector3(
                0.0f, 0.0f, (SeatsRoot as TableSeatAnchors)?.StandBackDistance ?? 0.55f));
        _cachedStandYaw = standExit?.GlobalRotation.Y ?? seat.GlobalRotation.Y;
        _hasCachedStandExit = true;
    }

    public override void ApplyControl(string turnOwnerId, Dictionary context)
    {
        // A turn change is purely a repaint. It must not pull somebody who is walking back into the
        // chair, and every participant remains allowed to return regardless of whose turn it is.
        CanTakeControl = Table?.IsMatchActive == true;
        RefreshView();
    }
}
