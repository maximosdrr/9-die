using Godot;

/// <summary>Production first-person arms authored from the same Blender actions as the body.</summary>
[GlobalClass]
public partial class PlayerFirstPersonHands : PokerHandVisual
{
    [Export] public Node3D ImportedRig;
    [Export] public Skeleton3D Skeleton;
    [Export] public Node3D CardGrip;
    [Export] public Node3D AuthoredCardGripMarker;

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
            var idle = Animator.GetAnimation(IdleClip);
            if (idle != null)
                idle.LoopMode = Animation.LoopModeEnum.Linear;
        }

        UpdateCardGrip();
    }

    public override void _Process(double delta) => UpdateCardGrip();

    public Transform3D CardGripGlobalTransform =>
        CardGrip?.GlobalTransform ?? GlobalTransform;

    public void UpdateCardGrip()
    {
        CharacterVisual.UpdateAuthoredCardGrip(Skeleton, CardGrip, AuthoredCardGripMarker);
    }

    private static void ConfigureFirstPersonRendering(Node node)
    {
        if (node is MeshInstance3D meshInstance)
            meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        foreach (var child in node.GetChildren())
            ConfigureFirstPersonRendering(child);
    }
}
