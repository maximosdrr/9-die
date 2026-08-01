using Godot;
using Godot.Collections;

[GlobalClass]
public partial class PlayerStrike : State
{
	public Player Player;
	[Export] public AnimationPlayer AnimationPlayer;

	public PlayerStrike()
	{
		Type = StatesRef.PlayerStrike;
	}

	public override void Setup(Node3D parentNode)
	{
		Player = parentNode as Player;
	}

	public override void Enter(Dictionary metadata)
	{
		AnimationPlayer.Play("Idle");
	}
}
