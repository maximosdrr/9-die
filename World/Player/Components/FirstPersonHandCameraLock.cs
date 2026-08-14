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

    [ExportGroup("Left hand follow limits")]
    /// <summary>Looking right pulls the left arm across the torso, so this is the tight side.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,or_greater")]
    public float LeftYawTowardBodyDegrees = 18.0f;

    /// <summary>Looking left moves the left arm away from the torso and allows a wider arc.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,or_greater")]
    public float LeftYawOutwardDegrees = 28.0f;

    /// <summary>The authored pose already rests at the table and is the absolute lower limit.</summary>
    [Export(PropertyHint.Range, "0,45,0.5,or_greater")]
    public float LeftPitchDownDegrees = 0.0f;

    [Export(PropertyHint.Range, "0,60,0.5,or_greater")]
    public float LeftPitchUpDegrees = 20.0f;

    [ExportGroup("Right hand follow limits")]
    // Mirrored defaults keep this component reusable if a future gesture follows the right hand.
    [Export(PropertyHint.Range, "0,60,0.5,or_greater")]
    public float RightYawOutwardDegrees = 28.0f;

    [Export(PropertyHint.Range, "0,60,0.5,or_greater")]
    public float RightYawTowardBodyDegrees = 18.0f;

    [Export(PropertyHint.Range, "0,45,0.5,or_greater")]
    public float RightPitchDownDegrees = 0.0f;

    [Export(PropertyHint.Range, "0,60,0.5,or_greater")]
    public float RightPitchUpDegrees = 20.0f;

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
        var skeletonBasis = skeleton.GlobalBasis.Orthonormalized();

        if (LeftFollowWeight > 0.0f)
            ApplyToArm(skeleton, _leftArmRoot,
                WeightedSkeletonDelta(
                    LimitedWorldCameraDelta(lockedBasis, liveBasis, leftHand: true),
                    skeletonBasis,
                    LeftFollowWeight));
        if (RightFollowWeight > 0.0f)
            ApplyToArm(skeleton, _rightArmRoot,
                WeightedSkeletonDelta(
                    LimitedWorldCameraDelta(lockedBasis, liveBasis, leftHand: false),
                    skeletonBasis,
                    RightFollowWeight));
    }

    /// <summary>
    /// Limits the arm in camera space without limiting the camera itself. X is yaw: negative looks
    /// right and positive looks left. Y is pitch relative to the authored resting view.
    /// </summary>
    internal Vector2 LimitFollowAnglesDegrees(Vector2 rawAngles, bool leftHand)
    {
        var negativeYawLimit = leftHand
            ? LeftYawTowardBodyDegrees
            : RightYawOutwardDegrees;
        var positiveYawLimit = leftHand
            ? LeftYawOutwardDegrees
            : RightYawTowardBodyDegrees;
        var downLimit = leftHand ? LeftPitchDownDegrees : RightPitchDownDegrees;
        var upLimit = leftHand ? LeftPitchUpDegrees : RightPitchUpDegrees;

        return new Vector2(
            SoftLimitAxis(rawAngles.X, negativeYawLimit, positiveYawLimit),
            SoftLimitAxis(rawAngles.Y, downLimit, upLimit));
    }

    internal Basis LimitedWorldCameraDelta(
        Basis lockedBasis, Basis liveBasis, bool leftHand)
    {
        lockedBasis = lockedBasis.Orthonormalized();
        liveBasis = liveBasis.Orthonormalized();

        var lockedForward = (-lockedBasis.Z).Normalized();
        var liveForward = (-liveBasis.Z).Normalized();
        var lockedFlat = new Vector3(lockedForward.X, 0.0f, lockedForward.Z).Normalized();
        var liveFlat = new Vector3(liveForward.X, 0.0f, liveForward.Z).Normalized();

        var yaw = lockedFlat.IsZeroApprox() || liveFlat.IsZeroApprox()
            ? 0.0f
            : lockedFlat.SignedAngleTo(liveFlat, Vector3.Up);
        var pitch = Mathf.Asin(Mathf.Clamp(liveForward.Y, -1.0f, 1.0f))
                    - Mathf.Asin(Mathf.Clamp(lockedForward.Y, -1.0f, 1.0f));
        var limited = LimitFollowAnglesDegrees(
            new Vector2(Mathf.RadToDeg(yaw), Mathf.RadToDeg(pitch)), leftHand);

        // Seated cameras are yawed in world space and pitched in their own local space. Rebuilding
        // in that same order avoids the axis coupling produced by decomposing a large Euler delta.
        var limitedLive = new Basis(Vector3.Up, Mathf.DegToRad(limited.X))
                          * lockedBasis
                          * new Basis(Vector3.Right, Mathf.DegToRad(limited.Y));
        return limitedLive * lockedBasis.Inverse();
    }

    private static float SoftLimitAxis(
        float value, float negativeLimitDegrees, float positiveLimitDegrees)
    {
        var limit = Mathf.Max(
            value < 0.0f ? negativeLimitDegrees : positiveLimitDegrees,
            0.0f);
        if (Mathf.IsZeroApprox(limit))
            return 0.0f;

        // tanh is linear around zero and progressively loses speed near the maximum. The hand
        // therefore settles naturally instead of snapping against a hard angular wall.
        var magnitude = Mathf.Abs(value);
        var limitedMagnitude = limit * (float)System.Math.Tanh(magnitude / limit);
        return value < 0.0f ? -limitedMagnitude : limitedMagnitude;
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
