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

    [ExportGroup("Optional finger clips")]
    [Export] public string IdleClip = "";
    [Export] public string PickUpClip = "";
    [Export] public string ChipsClip = "";
    [Export] public string KnockClip = "";
    [Export] public string FoldClip = "";
    [Export] public string RevealClip = "";

    public float Play(PokerGesture gesture)
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

        if (Animator == null || string.IsNullOrWhiteSpace(clip) || !Animator.HasAnimation(clip))
            return 0.0f;

        Animator.Play(clip);
        return (float)(Animator.GetAnimation(clip)?.Length ?? 0.0);
    }
}
