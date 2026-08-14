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
        var preparation = metadata != null && metadata.TryGetValue("preparation", out var prep)
            ? prep.AsString()
            : "";
        var idle = metadata != null && metadata.TryGetValue("idle", out var queuedIdle)
            ? queuedIdle.AsString()
            : CharacterVisual.Clips.Idle;
        var clip = !string.IsNullOrWhiteSpace(requested) && AnimationPlayer.HasAnimation(requested)
            ? requested
            : idle;

        AnimationPlayer.Play(clip);
        if (!string.IsNullOrWhiteSpace(preparation)
            && AnimationPlayer.HasAnimation(preparation))
        {
            AnimationPlayer.Queue(preparation);
        }
        if (idle != clip && AnimationPlayer.HasAnimation(idle))
            AnimationPlayer.Queue(idle);

        Player?.PlayFirstPersonSequence(clip, preparation, idle);
    }
}
