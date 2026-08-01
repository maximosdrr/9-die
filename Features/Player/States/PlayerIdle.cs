using Godot;
using Godot.Collections;

[GlobalClass]
public partial class PlayerIdle : State
{
	public Player Player;
	[Export] public AnimationPlayer AnimationPlayer;

	public PlayerIdle()
	{
		Type = StatesRef.PlayerIdle;
	}

	public override void Setup(Node3D parentNode)
	{
		Player = parentNode as Player;
	}

	public override void Enter(Dictionary metadata)
	{
		AnimationPlayer.Play("Idle");
	}

	public override void Process(double delta)
	{
		if (!Player.IsMultiplayerAuthority())
			return;

		if (Player.Velocity.Length() > 0.01f)
			StateMachine.ChangeState(StatesRef.PlayerWalking, new Dictionary());
	}
}
