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
	[Export] public float TopHeight = 0.5f;

	public DominoGame Game;
	public Player Player;
	public GlobalCamera Camera;

	private DominoHandView _handView;
	private Node3D _lookRig;
	private Node3D _lookPitch;
	private RemoteTransform3D _remoteSeat;
	private Node3D _topRig;
	private RemoteTransform3D _remoteTop;

	private float _seatYaw;
	private float _lookYaw;
	private float _lookPitch2;
	private bool _isLooking;
	private bool _inTopView;
	private bool _seated;

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
		// A window that loses focus mid-drag never delivers the button release, which would leave
		// the cursor captured and the hand unclickable with no way back.
		if (_isLooking && !Input.IsActionPressed("free_look"))
			StopLooking();
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
		if (_isLooking)
			InputFocus.Release();

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

		_seated = true;
		_inTopView = false;
		SetProcessUnhandledInput(true);
		SetProcess(true);
		ShowSeatView();

		// The opposite of the pool controller: the hand is clicked, so the cursor stays free and
		// looking around is a deliberate drag instead.
		InputFocus.Release();
	}

	/// <summary>
	/// Parks the overhead rig above the middle of the cloth, turned so the player's own side of
	/// the table is at the bottom of their screen. Each player gets their own orientation because
	/// the rig lives on the controller rather than in the shared table scene.
	/// </summary>
	private void PlaceTopRig()
	{
		var cloth = Game.ChainPresenter != null
			? Game.ChainPresenter.GlobalPosition
			: _lookRig.GlobalPosition;

		_topRig.GlobalPosition = cloth + new Vector3(0.0f, TopHeight, 0.0f);
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

		// Looking around is a seat-view gesture; leave the drag behind when the view changes.
		if (_isLooking)
			StopLooking();

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

		if (@event.IsActionPressed("toggle_top_view"))
		{
			ToggleTopView();
			GetViewport().SetInputAsHandled();
			return;
		}

		// Free look is a hold-to-drag: the cursor has to stay available for the hand, so the
		// mouse is only captured while the button is down.
		if (@event.IsActionPressed("free_look") && !_inTopView)
		{
			_isLooking = true;
			InputFocus.Capture();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (@event.IsActionReleased("free_look"))
		{
			StopLooking();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_isLooking && @event is InputEventMouseMotion motion)
		{
			ApplyLook(motion.Relative);
			GetViewport().SetInputAsHandled();
		}
	}

	private void StopLooking()
	{
		if (!_isLooking)
			return;

		_isLooking = false;
		InputFocus.Release();
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

		StopLooking();
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

	private void OnDrawRequested() => Game?.Resolver?.RequestDrawTile(Game.TurnToken);

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
