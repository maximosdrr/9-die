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
		var requested = metadata != null && metadata.TryGetValue("animation", out var animation)
			? animation.AsString()
			: "";
		var clip = !string.IsNullOrWhiteSpace(requested) && AnimationPlayer.HasAnimation(requested)
			? requested
			: "Idle";

		AnimationPlayer.Play(clip);
	}
}
