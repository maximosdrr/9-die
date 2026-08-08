using System.Collections.Generic;
using Domino.Rules;
using Godot;
using Godot.Collections;

/// <summary>
/// The per-player surface of a domino match: takes the seat, owns the two camera views and relays
/// what the hand view asks for to the server.
///
/// Unlike the pool controller, nothing moves when the turn changes — everyone stays seated for the
/// whole match and only the hand view's state changes. That is why "switch control" is off: there
/// is nothing to switch to until the match ends.
///
/// Two views, both driven from here rather than from a fixed marker in the table scene, so each
/// player looks around independently and sees the table from their own side:
///   - the SEAT view, a first-person head the player can turn by holding the free-look button;
///   - the TOP view, straight down over the table, rotated so their own side is nearest.
/// </summary>
[GlobalClass]
public partial class DominoController : GameController
{
	/// <summary>
	/// Swap this to change the entire hand presentation. The 3D rack view drops in here with no
	/// other change anywhere — see <see cref="DominoHandView"/> for the contract it must keep.
	/// </summary>
	[Export] public PackedScene HandViewScene;

	[ExportGroup("Seat view")]
	/// <summary>
	/// The same angle as walking. A tighter one magnifies the table but reads as a tunnel, so
	/// legibility is bought with tile size instead.
	/// </summary>
	[Export] public float SeatFov = 55.0f;
	[Export] public float MouseSensitivity = 0.004f;

	/// <summary>How far the head turns to either side before a real person would move their body.</summary>
	[Export] public float MaxYawDeg = 100.0f;

	[Export] public float MinPitchDeg = -70.0f;
	[Export] public float MaxPitchDeg = 25.0f;

	/// <summary>Where the head rests: tilted down at the table, which is what the player wants to see.</summary>
	[Export] public float RestPitchDeg = -32.0f;

	[ExportGroup("Top view")]
	[Export] public float TopFov = 55.0f;

	/// <summary>Height above the cloth. Framed so the whole playing area fills the shot at TopFov.</summary>
	[Export] public float TopHeight = 0.58f;

	/// <summary>
	/// How far the overhead view slides per pixel of mouse movement. Without panning, a crosshair
	/// locked to the screen centre would only ever point at the middle of the table.
	/// </summary>
	[Export] public float TopPanSensitivity = 0.0012f;

	/// <summary>How far the overhead view may wander from the middle of the cloth.</summary>
	[Export] public Vector2 TopPanLimit = new(0.34f, 0.34f);

	[ExportGroup("Leaving")]
	/// <summary>Held, not tapped: getting up mid-match forfeits, so it must not be a slip.</summary>
	[Export] public float LeaveHoldSeconds = 1.0f;

	public DominoGame Game;
	public Player Player;
	public GlobalCamera Camera;

	private const string InputTopView = "toggle_top_view";
	private const string InputLeave = "leave_table";

	private DominoHandView _handView;
	private Node3D _lookRig;
	private Node3D _lookPitch;
	private RemoteTransform3D _remoteSeat;
	private Node3D _topRig;
	private RemoteTransform3D _remoteTop;

	private float _seatYaw;
	private float _lookYaw;
	private float _lookPitch2;
	private bool _inTopView;
	private bool _seated;
	private Vector2 _topPan;
	private Vector3 _clothCentre;
	private float _leaveHeld;

	private static readonly List<MoveOption> NoMoves = new();

	public override bool AllowsControlSwitch => false;

	public override void _Ready()
	{
		_lookRig = GetNode<Node3D>("LookRig");
		_lookPitch = GetNode<Node3D>("LookRig/LookPitch");
		_remoteSeat = GetNode<RemoteTransform3D>("LookRig/LookPitch/RemoteSeat");
		_topRig = GetNode<Node3D>("TopRig");
		_remoteTop = GetNode<RemoteTransform3D>("TopRig/RemoteTop");

		// Both rigs are placed in world space from the seat, so they must not inherit the player's
		// transform — the same trick AimCameraPivot uses to ride the cue ball.
		_lookRig.TopLevel = true;
		_topRig.TopLevel = true;

		SetProcessUnhandledInput(false);
		SetProcess(false);
	}

	public override void _Process(double delta)
	{
		if (!_seated || !IsMultiplayerAuthority())
			return;

		// Getting up is a hold rather than a press, and the progress is shown, so a forfeit is
		// always a decision the player watched themselves make.
		if (Input.IsActionPressed(InputLeave))
		{
			_leaveHeld += (float)delta;

			if (_leaveHeld >= LeaveHoldSeconds)
			{
				_leaveHeld = 0.0f;
				_handView?.ShowNotice("Saindo da mesa", 2.0f);
				OnSurrenderRequested();
				return;
			}

			var remaining = Mathf.Max(0.0f, LeaveHoldSeconds - _leaveHeld);
			_handView?.ShowNotice($"Segure para sair da mesa… {remaining:F1}s", 0.2f);
			return;
		}

		_leaveHeld = 0.0f;
	}

	public override void Setup(Player parent, TableGame tableGame, GlobalCamera camera)
	{
		Player = parent;
		Game = tableGame as DominoGame;
		Camera = camera;

		if (Game == null)
		{
			GD.PushError("DominoController equipado num jogo que não é dominó.");
			return;
		}

		SignalUtil.ConnectGuarded(Game, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
		SignalUtil.ConnectGuarded(Game, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
		SignalUtil.ConnectGuarded(Game, DominoGame.SignalName.HudStateUpdated, new Callable(this, MethodName.Refresh));
		SignalUtil.ConnectGuarded(Game, DominoGame.SignalName.LocalHandChanged, new Callable(this, MethodName.Refresh));

		if (Game.Resolver != null)
		{
			SignalUtil.ConnectGuarded(Game.Resolver, DominoTurnResolver.SignalName.ActionRejected,
				new Callable(this, MethodName.OnActionRejected));
		}

		if (!IsMultiplayerAuthority())
			return;

		SpawnHandView();
		TakeSeat();
		Refresh();
	}

	public override void _ExitTree()
	{
		if (Game == null)
			return;

		SignalUtil.DisconnectGuarded(Game, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChanged));
		SignalUtil.DisconnectGuarded(Game, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
		SignalUtil.DisconnectGuarded(Game, DominoGame.SignalName.HudStateUpdated, new Callable(this, MethodName.Refresh));
		SignalUtil.DisconnectGuarded(Game, DominoGame.SignalName.LocalHandChanged, new Callable(this, MethodName.Refresh));

		if (Game.Resolver != null)
		{
			SignalUtil.DisconnectGuarded(Game.Resolver, DominoTurnResolver.SignalName.ActionRejected,
				new Callable(this, MethodName.OnActionRejected));
		}
	}

	private void SpawnHandView()
	{
		if (HandViewScene == null)
		{
			GD.PushError("DominoController sem HandViewScene: o jogador não terá como jogar.");
			return;
		}

		_handView = HandViewScene.Instantiate<DominoHandView>();
		_handView.Name = "HandView";

		// Authority has to be set before the view enters the tree, because its _Ready hides itself
		// on peers that do not own it.
		_handView.SetMultiplayerAuthority(Player.Id);
		AddChild(_handView);

		_handView.Setup(Game, Player);
		_handView.TilePlayRequested += OnTilePlayRequested;
		_handView.DrawRequested += OnDrawRequested;
		_handView.PassRequested += OnPassRequested;
		_handView.SurrenderRequested += OnSurrenderRequested;
	}

	// ---------------------------------------------------------------- seating and views

	/// <summary>
	/// Sits the player down and builds both camera rigs around the seat. This runs on the owning
	/// peer rather than the server because Player replicates its own position outward — a
	/// server-side teleport would be overwritten by the owner's next update.
	/// </summary>
	private void TakeSeat()
	{
		// Every view change below is null-guarded, so without this the player is seated and then
		// left staring wherever they were facing, with no clue why. Loud, because the cause is a
		// wiring mistake in the level rather than anything the player did.
		if (Camera == null)
		{
			GD.PushError($"Mesa de dominó sem câmera: o jogador {Player.Name} vai sentar mas a "
						 + "visão não vai mudar. Main precisa chamar SetCamera nesta mesa.");
		}

		var seat = Game.SeatFor((string)Player.Name);
		if (seat == null)
		{
			GD.PushWarning($"Sem assento para o jogador {Player.Name}; ele fica em pé onde estava.");
			return;
		}

		// PlayerGameHandler hides every controller it equips, which is right for the pool cue but
		// would leave the camera rigs inside a hidden subtree. Nothing here is visible anyway.
		Show();

		Player.GlobalPosition = seat.GlobalPosition;
		// Yaw only: a seat marker tilted to frame the camera must not tip the player over.
		_seatYaw = seat.GlobalRotation.Y;
		Player.GlobalRotation = new Vector3(0.0f, _seatYaw, 0.0f);
		Player.Velocity = Vector3.Zero;

		Player.GiveControl();
		Player.EnterGameControllerMode();

		// The eye point comes from the seat's own marker so an artist can raise or lower it per
		// chair without touching code.
		var eye = seat.GetNodeOrNull<Node3D>("SeatView");
		_lookRig.GlobalPosition = eye?.GlobalPosition ?? seat.GlobalPosition;

		_lookYaw = 0.0f;
		_lookPitch2 = Mathf.DegToRad(RestPitchDeg);
		ApplyLookRotation();

		PlaceTopRig();

		_clothCentre = Game.ChainPresenter != null
			? Game.ChainPresenter.GlobalPosition
			: _lookRig.GlobalPosition;
		_topPan = Vector2.Zero;

		_seated = true;
		_inTopView = false;
		_leaveHeld = 0.0f;
		SetProcessUnhandledInput(true);
		SetProcess(true);
		ShowSeatView();

		// Captured for the whole match: nothing is clicked any more, the hand is driven from the
		// keyboard and the aim is the centre of the screen, so looking around is just moving the
		// mouse. Escape gives the cursor back when the player needs to leave the window.
		InputFocus.Capture();
	}

	/// <summary>
	/// Parks the overhead rig above the middle of the cloth, turned so the player's own side of
	/// the table is at the bottom of their screen. Each player gets their own orientation because
	/// the rig lives on the controller rather than in the shared table scene.
	/// </summary>
	private void PlaceTopRig()
	{
		// The pan is applied in the player's own frame, so pushing the mouse right always slides
		// the view right from where they are sitting, whichever side of the table that is.
		var offset = new Vector3(_topPan.X, TopHeight, _topPan.Y).Rotated(Vector3.Up, _seatYaw);

		_topRig.GlobalPosition = _clothCentre + offset;
		_topRig.GlobalRotation = new Vector3(-Mathf.Pi * 0.5f, _seatYaw, 0.0f);
	}

	private void ApplyLookRotation()
	{
		_lookRig.GlobalRotation = new Vector3(0.0f, _seatYaw + _lookYaw, 0.0f);
		_lookPitch.Rotation = new Vector3(_lookPitch2, 0.0f, 0.0f);
	}

	private void ShowSeatView()
	{
		Camera?.SetGlobalCameraFov(SeatFov);
		Camera?.TransitionTo(_remoteSeat);
	}

	private void ShowTopView()
	{
		PlaceTopRig();
		Camera?.SetGlobalCameraFov(TopFov);
		Camera?.TransitionTo(_remoteTop);
	}

	public void ToggleTopView()
	{
		if (!_seated || !IsMultiplayerAuthority())
			return;

		_inTopView = !_inTopView;
		_topPan = Vector2.Zero;

		if (_inTopView)
			ShowTopView();
		else
			ShowSeatView();

		_handView?.SetTopViewActive(_inTopView);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!_seated || !IsMultiplayerAuthority())
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

		if (_inTopView)
			PanTopView(motion.Relative);
		else
			ApplyLook(motion.Relative);

		GetViewport().SetInputAsHandled();
	}

	/// <summary>
	/// Slides the overhead view across the cloth. The crosshair never leaves the middle of the
	/// screen, so moving the camera is how the player reaches the far end of the chain and the
	/// stock from above.
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
		if (!IsMultiplayerAuthority())
			return;

		_seated = false;
		SetProcessUnhandledInput(false);
		SetProcess(false);

		_handView?.SetInteractive(false);
		Camera?.ResetFov();
		Player?.ExitGameControllerMode();
	}

	public override void ApplyControl(string turnOwnerId, Dictionary context)
	{
		// Seated players never hand control back and forth mid-match, so a turn change is purely
		// a repaint.
		CanTakeControl = false;
		Refresh();
	}

	private void OnTurnChanged(string nextPlayerId, Dictionary context) => Refresh();

	private void OnTurnExtended(Dictionary context) => Refresh();

	private void Refresh()
	{
		if (_handView == null || Game == null || Player == null || !IsMultiplayerAuthority())
			return;

		var isYourTurn = IsInstanceValid(Game.TurnOwner)
						 && (string)Game.TurnOwner.Name == (string)Player.Name;

		// The same pure functions the server re-runs on whatever comes back, so the interface can
		// never offer a move the server would reject.
		var moves = isYourTurn
			? DominoRules.LegalMoves(Game.LocalHand, Game.LeftEnd, Game.RightEnd)
			: NoMoves;
		var canDraw = isYourTurn
					  && DominoRules.CanDraw(Game.LocalHand, Game.LeftEnd, Game.RightEnd, Game.BoneyardCount);
		var mustPass = isYourTurn
					   && DominoRules.MustPass(Game.LocalHand, Game.LeftEnd, Game.RightEnd, Game.BoneyardCount);

		_handView.Refresh(Game.LocalHand, moves, isYourTurn, canDraw, mustPass);
	}

	private void OnTilePlayRequested(int tileId, int end) =>
		Game?.Resolver?.RequestPlayTile(Game.TurnToken, tileId, end);

	private void OnDrawRequested(int slot) => Game?.Resolver?.RequestDrawTile(Game.TurnToken, slot);

	private void OnPassRequested() => Game?.Resolver?.RequestPass(Game.TurnToken);

	private void OnSurrenderRequested() => Game?.RequestSurrender((string)Player.Name);

	private void OnActionRejected(string reason)
	{
		if (!IsMultiplayerAuthority())
			return;

		_handView?.ShowRejection(reason);
		// The rejection may have been "the turn already moved", so repaint from real state rather
		// than leaving the interface showing what the player thought was true.
		Refresh();
	}
}
