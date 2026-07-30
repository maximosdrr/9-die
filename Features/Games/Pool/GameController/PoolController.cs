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

	public override void _Ready()
	{
		RemoteAim = GetNode<RemoteTransform3D>("AimPivot/Elevation/RemoteAim");
		AimPivot = GetNode<AimCameraPivot>("AimPivot");
		Cue = GetNode<Cue>("AimPivot/Cue");
	}

	public void Setup(Player parent, TableGame tableGame)
	{
		Player = parent;
		PoolGame = tableGame as PoolGame;

		_ = AimPivot.Setup(PoolGame, this);
		Cue.Setup(PoolGame, AimPivot);

		PoolGame.TurnChanged += OnTurnChange;
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

		Global.Instance.Camera?.SetGlobalCameraFov(60);
		Global.Instance.Camera?.TransitionTo(RemoteAim);
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public void GiveControl()
	{
		if (!IsMultiplayerAuthority())
			return;

		Hide();
		Global.Instance.Camera?.SetGlobalCameraFov(75);
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
			if (!context.ContainsKey("ball_replacement"))
			{
				TakeControl();
			}
			else
			{
				await ToSignal(PoolGame.BallPlacementManager, BallPlacementManager.SignalName.PlacementFinished);
				await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
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
}
