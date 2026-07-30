using Godot;

[GlobalClass]
public partial class GlobalCamera : Camera3D
{
    [ExportCategory("Properties")]
    [Export] public float DefaultFov = 75.0f;

    public RemoteTransform3D CurrentRemote = null;

    public void SetGlobalCameraFov(float newFov)
    {
        Fov = newFov;
    }

    public void ResetFov()
    {
        Fov = DefaultFov;
    }

    public override void _EnterTree()
    {
        Global.Instance.Camera = this;
    }

    public override void _ExitTree()
    {
        if (Global.Instance.Camera == this)
            Global.Instance.Camera = null;
    }

    public void TransitionTo(RemoteTransform3D newRemote)
    {
        if (CurrentRemote != null && IsInstanceValid(CurrentRemote))
            DisableRemote(CurrentRemote);

        CurrentRemote = newRemote;

        if (CurrentRemote != null)
        {
            if (CurrentRemote.RemotePath != GetPath())
                CurrentRemote.RemotePath = GetPath();

            EnableRemote(CurrentRemote);
        }
    }

    private void DisableRemote(RemoteTransform3D remote)
    {
        remote.UpdatePosition = false;
        remote.UpdateRotation = false;
        remote.UpdateScale = false;
    }

    private void EnableRemote(RemoteTransform3D remote)
    {
        remote.UseGlobalCoordinates = true;
        remote.UpdatePosition = true;
        remote.UpdateRotation = true;
        remote.UpdateScale = false;
    }
}
