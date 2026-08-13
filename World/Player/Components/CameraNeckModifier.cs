using Godot;

/// <summary>Layers a bounded camera look over the final animated pose.</summary>
[Tool]
[GlobalClass]
public partial class CameraNeckModifier : SkeletonModifier3D
{
    [Export] public string BoneName = "CC_Base_NeckTwist02";
    [Export(PropertyHint.Range, "0,1,0.01")] public float LookInfluence = 0.72f;
    [Export] public float Response = 12.0f;

    private int _bone = -1;
    private Vector2 _target;
    private Vector2 _current;

    public override void _ProcessModificationWithDelta(double delta)
    {
        var skeleton = GetSkeleton();
        if (skeleton == null)
            return;

        if (_bone < 0)
            _bone = skeleton.FindBone(BoneName);
        if (_bone < 0)
            return;

        var response = 1.0f - Mathf.Exp(-Mathf.Max(Response, 0.01f) * (float)delta);
        _current = _current.Lerp(_target, response);

        var pose = skeleton.GetBoneGlobalPose(_bone);
        var yaw = new Basis(Vector3.Up, _current.X * LookInfluence);
        // Camera and neck use opposite pitch conventions in this imported rig. Without the sign
        // conversion, looking up through the camera makes the avatar bow its head for other peers.
        var pitch = new Basis(Vector3.Right,
            RigPitchFromCamera(_current.Y) * LookInfluence);
        pose.Basis = yaw * pitch * pose.Basis;
        skeleton.SetBoneGlobalPose(_bone, pose);
    }

    public void SetLook(Vector2 look) => _target = look;

    public void ResetLook() => _target = Vector2.Zero;

    internal static float RigPitchFromCamera(float cameraPitch) => -cameraPitch;
}
