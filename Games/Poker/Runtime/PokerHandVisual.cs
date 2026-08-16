using Godot;

/// <summary>
/// Optional adapter for a replaceable first-person hand scene.
///
/// The parent view always supplies the large arm movement. A future rig can use this component to
/// add finger poses without making the poker controller know anything about its skeleton or the
/// names imported from Blender.
/// </summary>
[GlobalClass]
public partial class PokerHandVisual : Node3D
{
    [Export] public AnimationPlayer Animator;

    [ExportGroup("Animation clips")]
    [Export] public string IdleClip = "";
    [Export] public string LookClip = "";
    [Export] public string PickUpClip = "";
    [Export] public string ChipsClip = "";
    [Export] public string KnockClip = "";
    [Export] public string FoldClip = "";
    [Export] public string RevealClip = "";

    public float Play(PokerGesture gesture, double blend = 0.18, float speed = 1.0f)
    {
        var clip = gesture switch
        {
            PokerGesture.PickUpCards => PickUpClip,
            PokerGesture.ThrowChips => ChipsClip,
            PokerGesture.Knock => KnockClip,
            PokerGesture.Fold => FoldClip,
            PokerGesture.Reveal => RevealClip,
            _ => IdleClip,
        };

        return PlayClip(clip, blend, speed);
    }

    public float PlayClip(string clip, double blend = 0.18, float speed = 1.0f)
    {
        if (Animator == null || string.IsNullOrWhiteSpace(clip) || !Animator.HasAnimation(clip))
            return 0.0f;

        Animator.Play(clip, blend, speed);
        Animator.Advance(0.0);
        return (float)(Animator.GetAnimation(clip)?.Length ?? 0.0);
    }
}
