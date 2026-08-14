using Godot;

/// <summary>Production headless first-person body authored from the same Blender actions as 3P.</summary>
[GlobalClass]
public partial class PlayerFirstPersonHands : PokerHandVisual
{
    [Export] public Node3D ImportedRig;
    [Export] public Skeleton3D Skeleton;
    [Export] public Node3D CardGrip;
    [Export] public Node3D AuthoredCardGripMarker;
    [Export] public FirstPersonHandCameraLock CameraLock;

    private Node3D _cardSlotsFollower;

    public override void _Ready()
    {
        base._Ready();
        ProcessPriority = 100;

        // The animation and camera placement keep the forearms above the table. Depth testing must
        // remain enabled: disabling it makes rear sleeve/glove polygons render over the skin and
        // produces the apparently twisted mesh that used to show up in first person.
        if (ImportedRig != null)
            ConfigureFirstPersonRendering(ImportedRig);

        if (Animator != null)
        {
            SetLoop(IdleClip);
            SetLoop(LookClip);
        }

        // SkeletonModifier3D is evaluated after ordinary _Process callbacks. Reading the hand pose
        // only from _Process therefore leaves held cards one modifier-step behind the rendered arm
        // while it follows the camera. SkeletonUpdated is emitted after every modifier has finished,
        // which makes it the authoritative point for hand attachments.
        if (Skeleton != null)
            Skeleton.SkeletonUpdated += OnSkeletonUpdated;

        UpdateCardGrip();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Skeleton))
            Skeleton.SkeletonUpdated -= OnSkeletonUpdated;
    }

    public override void _Process(double delta) => UpdateCardGrip();

    public Transform3D CardGripGlobalTransform =>
        CardGrip?.GlobalTransform ?? GlobalTransform;

    public void UpdateCardGrip()
    {
        CharacterVisual.UpdateAuthoredCardGrip(Skeleton, CardGrip, AuthoredCardGripMarker);

        if (GodotObject.IsInstanceValid(_cardSlotsFollower)
            && GodotObject.IsInstanceValid(CardGrip))
        {
            _cardSlotsFollower.GlobalTransform = CardGrip.GlobalTransform;
        }
    }

    /// <summary>
    /// Registers the gameplay card root that must follow the final left-hand pose. It stays outside
    /// the imported rig so the same physical PokerCard nodes can still return to the table.
    /// </summary>
    public void BindCardSlots(Node3D cardSlots)
    {
        _cardSlotsFollower = cardSlots;
        UpdateCardGrip();
    }

    private void OnSkeletonUpdated() => UpdateCardGrip();

    /// <summary>Applies one reusable camera policy to each arm independently.</summary>
    public void ConfigureHandCameraModes(
        Transform3D lockedView,
        Node3D liveView,
        FirstPersonHandCameraMode leftMode,
        FirstPersonHandCameraMode rightMode)
    {
        CameraLock?.Configure(lockedView, liveView, leftMode, rightMode);
    }

    public void LockBothHands(bool immediate = false) => CameraLock?.LockBoth(immediate);

    private void SetLoop(string clip)
    {
        var animation = Animator?.GetAnimation(clip);
        if (animation != null)
            animation.LoopMode = Animation.LoopModeEnum.Linear;
    }

    private static void ConfigureFirstPersonRendering(Node node)
    {
        if (node is MeshInstance3D meshInstance)
            meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        foreach (var child in node.GetChildren())
            ConfigureFirstPersonRendering(child);
    }
}
