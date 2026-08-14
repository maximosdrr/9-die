using Godot;
using Godot.Collections;

[GlobalClass]
public partial class PlayerWalking : State
{
    public Player Player;
    [Export] public AnimationPlayer AnimationPlayer;

    public PlayerWalking()
    {
        Type = StatesRef.PlayerWalking;
    }

    public override void Setup(Node3D parentNode)
    {
        Player = parentNode as Player;
    }

    public override void Enter(Dictionary metadata)
    {
        AnimationPlayer.Play(CharacterVisual.Clips.Walk);
        Player?.PlayFirstPersonAnimation(CharacterVisual.Clips.Walk);
    }

    public override void Process(double delta)
    {
        if (!Player.IsMultiplayerAuthority())
            return;

        if (Player.Velocity.Length() <= 0.01f)
            StateMachine.ChangeState(StatesRef.PlayerIdle, new Dictionary());
    }
}
