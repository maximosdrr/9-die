using Godot;

[GlobalClass]
public partial class HeadPivot : Node3D
{
    [Export] public RemoteTransform3D CameraMount;

    public CharacterBody3D PlayerBody;

    [ExportGroup("Settings")]
    [Export] public float MouseSensitivity = 0.005f;
    [Export] public float MinPitch = -65.0f;
    [Export] public float MaxPitch = 60.0f;

    public override void _Ready()
    {
        var node = GetParent();
        while (node != null)
        {
            if (node is CharacterBody3D body)
            {
                PlayerBody = body;
                break;
            }
            node = node.GetParent();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsMultiplayerAuthority())
            return;

        if (@event.IsActionPressed("ui_cancel"))
        {
            InputFocus.Release();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            if (!InputFocus.IsCaptured)
            {
                InputFocus.Capture();
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (!InputFocus.IsCaptured)
            return;

        if (@event is InputEventMouseMotion motion)
            HandleCameraRotation(motion);
    }

    private void HandleCameraRotation(InputEventMouseMotion @event)
    {
        if (PlayerBody != null)
            PlayerBody.RotateY(-@event.Relative.X * MouseSensitivity);

        RotateX(-@event.Relative.Y * MouseSensitivity);

        var rot = Rotation;
        rot.X = Mathf.Clamp(rot.X, Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));
        Rotation = rot;
    }
}
