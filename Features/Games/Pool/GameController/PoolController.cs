using Godot;
using Godot.Collections;

[GlobalClass]
public partial class PoolController : GameController
{
	public RemoteTransform3D RemoteAim;
	public AimCameraPivot AimPivot;
	public Cue Cue;

	public PoolGame PoolGame;
	public Player Player;
	public GlobalCamera Camera;

	public override void _Ready()
	{
		RemoteAim = GetNode<RemoteTransform3D>("AimPivot/Elevation/RemoteAim");
		AimPivot = GetNode<AimCameraPivot>("AimPivot");
		Cue = GetNode<Cue>("AimPivot/Cue");
	}

	public override void Setup(Player parent, TableGame tableGame, GlobalCamera camera)
	{
		Player = parent;
		PoolGame = tableGame as PoolGame;
		Camera = camera;

		AimPivot.Setup(PoolGame, this);
		Cue.Setup(PoolGame, AimPivot);

		SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChange));
		SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
	}

	public override void _ExitTree()
	{
		if (PoolGame == null)
			return;

		SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChange));
		SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
	}

	public override void TakeControl()
	{
		if (!IsMultiplayerAuthority())
			return;

		if (!IsInstanceValid(PoolGame) || !PoolGame.IsMatchActive || !IsInstanceValid(Player))
		{
			return;
		}

		if (!PoolGame.IsTurnOwner((string)Player.Name))
		{
			GD.PushError("Cannot take control, it's not your turn!");
			return;
		}

		Show();
		Cue?.RestoreAimingPresentation();
		SetProcessUnhandledInput(true);
		SetProcess(true);

		AimPivot.SetProcess(true);
		AimPivot.SetPhysicsProcess(true);
		AimPivot.SetProcessUnhandledInput(true);

		Player.EnterGameControllerMode();

		Camera?.SetGlobalCameraFov(60);
		Camera?.TransitionTo(RemoteAim);
		InputFocus.Capture();
	}

	public override void GiveControl()
	{
		if (!IsMultiplayerAuthority())
			return;

		Hide();
		Camera?.SetGlobalCameraFov(75);
		SetProcessUnhandledInput(false);
		SetProcess(false);

		AimPivot.SetProcess(false);
		AimPivot.SetPhysicsProcess(false);
		AimPivot.SetProcessUnhandledInput(false);

		Player.ExitGameControllerMode();
	}

	public override async void ApplyControl(string turnOwnerId, Dictionary context)
	{
		if (!IsInsideTree() || !IsInstanceValid(PoolGame) || !IsInstanceValid(Player))
			return;

		if (turnOwnerId == (string)Player.Name)
		{
			CanTakeControl = true;
			Player.GiveControl();
			if (context.TryGetValue("push_out_choice_pending", out var pendingChoice)
				&& pendingChoice.AsBool())
			{
				GiveControl();
			}
			else if (!context.ContainsKey("ball_replacement"))
			{
				TakeControl();
			}
			else
			{
				// A newly equipped controller starts visible. Hide the cue and disable aiming while
				// ball placement owns the camera and input, including before the opening break.
				GiveControl();
				await ToSignal(PoolGame.BallPlacementManager, BallPlacementManager.SignalName.PlacementFinished);
				if (!IsInsideTree() || !IsInstanceValid(PoolGame) || !PoolGame.IsMatchActive
					|| !IsInstanceValid(Player)
					|| !PoolGame.IsTurnOwner((string)Player.Name))
					return;

				TakeControl();
			}
		}
		else
		{
			CanTakeControl = false;
			GiveControl();
			Player.TakeControl();
		}
	}

	private void OnTurnChange(string nextPlayerName, Dictionary context)
	{
		ApplyControl(nextPlayerName, context);
	}

	private void OnTurnExtended(Dictionary context)
	{
		if (!IsMultiplayerAuthority() || !IsInstanceValid(PoolGame)
			|| !PoolGame.IsMatchActive || !IsInstanceValid(Player)
			|| !PoolGame.IsTurnOwner((string)Player.Name))
			return;

		if (context.ContainsKey("ball_replacement"))
		{
			ApplyControl((string)Player.Name, context);
			return;
		}

		if (context.TryGetValue("push_out_choice_resolved", out var resolved) && resolved.AsBool())
			TakeControl();
	}
}
