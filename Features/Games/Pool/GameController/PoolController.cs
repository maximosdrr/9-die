using Godot;
using Godot.Collections;

[GlobalClass]
public partial class PoolController : Node3D
{
	public bool CanTakeControl = false;

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

	public void Setup(Player parent, TableGame tableGame, GlobalCamera camera)
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

	public void TakeControl()
	{
		if (!IsMultiplayerAuthority())
			return;

		if (PoolGame.TurnOwner == null)
		{
			GD.PushError("Game not started yet! Table.turn_owner is null");
			return;
		}

		if (Player.Name != PoolGame.TurnOwner.Name)
		{
			GD.PushError("Cannot take control, it's not your turn!");
			return;
		}

		Show();
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

	public void GiveControl()
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

	public async void ApplyControl(string turnOwnerId, Dictionary context)
	{
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
				await ToSignal(PoolGame.BallPlacementManager, BallPlacementManager.SignalName.PlacementFinished);
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
		if (!IsMultiplayerAuthority() || PoolGame?.TurnOwner == null
			|| (string)PoolGame.TurnOwner.Name != (string)Player.Name)
			return;

		if (context.TryGetValue("push_out_choice_resolved", out var resolved) && resolved.AsBool())
			TakeControl();
	}
}
