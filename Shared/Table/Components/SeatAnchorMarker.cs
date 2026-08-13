using Godot;

/// <summary>
/// An artist-authored seat marker with an editor-only preview of the production character.
/// Its own transform is the body root, SeatView is the camera/eye, and StandExit is where the
/// walking body returns. None of the preview nodes exist while the game is running.
/// </summary>
[Tool]
[GlobalClass]
public partial class SeatAnchorMarker : Marker3D
{
    private const string CharacterScenePath =
        "res://World/Player/Components/CharacterVisual.tscn";

    private bool _showCharacterPreview = true;
    private string _previewAnimation = "IdleSitHoldingCards";
    private float _previewOpacity = 0.42f;
    private Node3D _preview;

    [ExportGroup("Editor preview")]
    [Export]
    public bool ShowCharacterPreview
    {
        get => _showCharacterPreview;
        set
        {
            _showCharacterPreview = value;
            QueuePreviewRefresh();
        }
    }

    [Export(PropertyHint.Enum,
        "IdleSitHoldingCards,IdleSit,SitHoldingCards,Sit,Idle,Walk")]
    public string PreviewAnimation
    {
        get => _previewAnimation;
        set
        {
            _previewAnimation = value;
            QueuePreviewRefresh();
        }
    }

    [Export(PropertyHint.Range, "0,0.9,0.05")]
    public float PreviewOpacity
    {
        get => _previewOpacity;
        set
        {
            _previewOpacity = Mathf.Clamp(value, 0.0f, 0.9f);
            QueuePreviewRefresh();
        }
    }

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            CallDeferred(MethodName.RefreshPreview);
    }

    private void QueuePreviewRefresh()
    {
        if (Engine.IsEditorHint() && IsInsideTree())
            CallDeferred(MethodName.RefreshPreview);
    }

    private void RefreshPreview()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
            return;

        if (IsInstanceValid(_preview))
        {
            _preview.Free();
            _preview = null;
        }

        if (!ShowCharacterPreview)
            return;

        var scene = GD.Load<PackedScene>(CharacterScenePath);
        if (scene?.Instantiate() is not Node3D character)
            return;

        character.Name = "CharacterPreview_EditorOnly";
        AddChild(character, forceReadableName: false, InternalMode.Back);
        _preview = character;

        foreach (var mesh in character.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (mesh is not MeshInstance3D geometry)
                continue;

            geometry.Transparency = PreviewOpacity;
            geometry.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        var animator = character.FindChild("AnimationPlayer", recursive: true,
            owned: false) as AnimationPlayer;
        if (animator?.HasAnimation(PreviewAnimation) == true)
        {
            animator.Play(PreviewAnimation);
            animator.Seek(1.0, update: true);
            animator.Advance(0.0);
        }
    }
}
