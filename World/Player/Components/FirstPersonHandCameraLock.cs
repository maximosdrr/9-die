using Godot;

/// <summary>How one first-person arm reacts while the player turns the seated camera.</summary>
public enum FirstPersonHandCameraMode
{
    /// <summary>The authored animation remains in its table/body-space pose.</summary>
    Locked = 0,

    /// <summary>The arm follows the camera around its shoulder while other body parts stay put.</summary>
    FollowCamera = 1,
}

/// <summary>
/// Layers camera motion over either arm independently, after the authored animation has been
/// evaluated. The character root can remain fixed to the chair while one selected hand follows
/// what the player is looking at.
/// </summary>
[Tool]
[GlobalClass]
public partial class FirstPersonHandCameraLock : SkeletonModifier3D
{
    // Upperarm keeps the shoulder socket fixed. Applying a large camera yaw to the clavicle would
    // pull and twist the torso mesh near the collar at the camera's +/-100 degree limit.
    [Export] public string LeftArmRootBone = "CC_Base_L_Upperarm";
    [Export] public string RightArmRootBone = "CC_Base_R_Upperarm";
    [Export] public float Response = 14.0f;

    public FirstPersonHandCameraMode LeftMode { get; private set; }
        = FirstPersonHandCameraMode.Locked;
    public FirstPersonHandCameraMode RightMode { get; private set; }
        = FirstPersonHandCameraMode.Locked;
    public float LeftFollowWeight { get; private set; }
    public float RightFollowWeight { get; private set; }

    private int _leftArmRoot = -1;
    private int _rightArmRoot = -1;
    private Transform3D _lockedView = Transform3D.Identity;
    private Node3D _liveView;

    /// <summary>
    /// Supplies the stable table-space camera frame and the actual live camera frame. Translation
    /// is deliberately ignored: seated camera input rotates the head and must never drag a hand
    /// away from the table.
    /// </summary>
    public void Configure(
        Transform3D lockedView,
        Node3D liveView,
        FirstPersonHandCameraMode leftMode,
        FirstPersonHandCameraMode rightMode)
    {
        LeftMode = leftMode;
        RightMode = rightMode;
        _lockedView = lockedView;
        _liveView = liveView;
    }

    public void LockBoth(bool immediate = false)
    {
        LeftMode = FirstPersonHandCameraMode.Locked;
        RightMode = FirstPersonHandCameraMode.Locked;
        if (immediate)
        {
            LeftFollowWeight = 0.0f;
            RightFollowWeight = 0.0f;
            _liveView = null;
        }
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        var response = 1.0f - Mathf.Exp(-Mathf.Max(Response, 0.01f) * (float)delta);
        LeftFollowWeight = Mathf.Lerp(
            LeftFollowWeight,
            LeftMode == FirstPersonHandCameraMode.FollowCamera ? 1.0f : 0.0f,
            response);
        RightFollowWeight = Mathf.Lerp(
            RightFollowWeight,
            RightMode == FirstPersonHandCameraMode.FollowCamera ? 1.0f : 0.0f,
            response);
        if (LeftFollowWeight < 0.0001f)
            LeftFollowWeight = 0.0f;
        if (RightFollowWeight < 0.0001f)
            RightFollowWeight = 0.0f;

        if (Mathf.IsZeroApprox(LeftFollowWeight)
            && Mathf.IsZeroApprox(RightFollowWeight))
        {
            return;
        }

        var skeleton = GetSkeleton();
        if (skeleton == null)
            return;

        ResolveBones(skeleton);
        var lockedBasis = _lockedView.Basis.Orthonormalized();
        var liveBasis = IsInstanceValid(_liveView)
            ? _liveView.GlobalBasis.Orthonormalized()
            : lockedBasis;
        var worldCameraDelta = liveBasis * lockedBasis.Inverse();
        var skeletonBasis = skeleton.GlobalBasis.Orthonormalized();

        if (LeftFollowWeight > 0.0f)
            ApplyToArm(skeleton, _leftArmRoot,
                WeightedSkeletonDelta(worldCameraDelta, skeletonBasis, LeftFollowWeight));
        if (RightFollowWeight > 0.0f)
            ApplyToArm(skeleton, _rightArmRoot,
                WeightedSkeletonDelta(worldCameraDelta, skeletonBasis, RightFollowWeight));
    }

    private void ResolveBones(Skeleton3D skeleton)
    {
        if (_leftArmRoot < 0)
            _leftArmRoot = skeleton.FindBone(LeftArmRootBone);
        if (_rightArmRoot < 0)
            _rightArmRoot = skeleton.FindBone(RightArmRootBone);
    }

    private static void ApplyToArm(Skeleton3D skeleton, int armRoot, Basis cameraDelta)
    {
        if (armRoot < 0)
            return;

        var pose = skeleton.GetBoneGlobalPose(armRoot);
        pose.Basis = cameraDelta * pose.Basis;
        skeleton.SetBoneGlobalPose(armRoot, pose);
    }

    private static Basis WeightedSkeletonDelta(
        Basis worldCameraDelta, Basis skeletonBasis, float weight)
    {
        var weightedWorld = new Basis(Quaternion.Identity.Slerp(
            worldCameraDelta.GetRotationQuaternion(), Mathf.Clamp(weight, 0.0f, 1.0f)));
        return skeletonBasis.Inverse() * weightedWorld * skeletonBasis;
    }
}
