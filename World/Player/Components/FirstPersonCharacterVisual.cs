using Godot;

/// <summary>
/// Local-only, headless version of the player body. It is rooted at the player's body origin so
/// legs and torso do not pitch with the camera, and it lives on a camera-only render layer.
/// </summary>
[GlobalClass]
public partial class FirstPersonCharacterVisual : Node3D
{
    public const uint RenderLayer = 4u;
    public const int RenderLayerBit = 2;

    [Export] public Node3D RigRoot;
    [Export] public AnimationPlayer Animator;

    public override void _Ready()
    {
        ConfigureRendering(this);
        SetLoop(CharacterVisual.Clips.Idle);
        SetLoop(CharacterVisual.Clips.Walk);
        SetLoop(CharacterVisual.Clips.IdleSit);
        SetLoop(CharacterVisual.Clips.IdleSitHoldingCards);
        SetLoop(CharacterVisual.Clips.IdleHoldingCardsDown);
        Play(CharacterVisual.Clips.Idle, 0.0);
    }

    public bool HasAnimation(string clip) =>
        Animator != null && !string.IsNullOrWhiteSpace(clip) && Animator.HasAnimation(clip);

    public void Play(string clip, double blend = 0.16, float speed = 1.0f)
    {
        if (!HasAnimation(clip))
            return;
        if (Animator.CurrentAnimation == clip && Animator.IsPlaying())
            return;

        Animator.Play(clip, blend, speed);
    }

    public void PlaySequence(
        string transition, string preparation, string idle, double blend = 0.18)
    {
        if (!HasAnimation(transition))
        {
            Play(idle, blend);
            return;
        }

        Animator.Play(transition, blend);
        if (HasAnimation(preparation))
            Animator.Queue(preparation);
        if (HasAnimation(idle))
            Animator.Queue(idle);
    }

    private void SetLoop(string clip)
    {
        var animation = Animator?.GetAnimation(clip);
        if (animation != null)
            animation.LoopMode = Animation.LoopModeEnum.Linear;
    }

    private static void ConfigureRendering(Node node)
    {
        if (node is VisualInstance3D visual)
        {
            visual.Layers = RenderLayer;
            if (visual is GeometryInstance3D geometry)
                geometry.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        foreach (var child in node.GetChildren())
            ConfigureRendering(child);
    }
}
