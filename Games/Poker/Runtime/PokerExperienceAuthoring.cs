using System;
using Godot;

/// <summary>
/// Artist-owned poker layout and camera configuration. The markers live in Poker.tscn, so moving
/// them in the 3D editor moves the same deck, board and local action guide used during play.
/// </summary>
[Tool]
[GlobalClass]
public partial class PokerExperienceAuthoring : Node3D
{
    [ExportGroup("Physical markers")]
    [Export] public Node3D DeckAnchor;
    [Export] public Node3D CommunityCardsAnchor;
    [Export] public Node3D ActionGuideAnchor;

    [ExportGroup("First-person preview")]
    [Export] public Node3D Seats;
    [Export] public Camera3D PreviewCamera;
    [Export] public Node3D FirstPersonHandsPreview;
    [Export] public Marker3D FirstPersonCardsPose;
    [Export] public Marker3D FirstPersonCard0Pose;
    [Export] public Marker3D FirstPersonCard1Pose;
    [Export(PropertyHint.Range, "-30,100,1")] public float RestCardsTiltDeg = 0.0f;
    [Export(PropertyHint.Range, "-30,100,1")] public float PeekCardsTiltDeg = -18.0f;
    [Export(PropertyHint.Range, "4,18,0.5")] public float HeldCardsFanStepDeg = 11.0f;
    [Export(PropertyHint.Range, "0.15,0.60,0.01")] public float HeldCardsFanRadius = 0.40f;

    [Export(PropertyHint.Range, "0,3,1")]
    public int PreviewSeat
    {
        get => _previewSeat;
        set
        {
            _previewSeat = Mathf.Clamp(value, 0, 3);
            QueuePreviewRefresh();
        }
    }

    [Export]
    public bool EnablePreviewCamera
    {
        get => _enablePreviewCamera;
        set
        {
            _enablePreviewCamera = value;
            QueuePreviewRefresh();
        }
    }

    [Export]
    public bool ShowFirstPersonHands
    {
        get => _showFirstPersonHands;
        set
        {
            _showFirstPersonHands = value;
            QueuePreviewRefresh();
        }
    }

    [Export]
    public bool RefreshPreview
    {
        get => false;
        set
        {
            if (value)
                QueuePreviewRefresh();
        }
    }

    [ExportGroup("Seat camera")]
    [Export(PropertyHint.Range, "30,100,1")] public float SeatFov = 55.0f;
    [Export] public Vector3 SeatViewOffset = Vector3.Zero;
    [Export(PropertyHint.Range, "-80,20,1")] public float RestPitchDeg = -35.0f;
    [Export(PropertyHint.Range, "20,140,1")] public float MaximumYawDeg = 100.0f;
    [Export(PropertyHint.Range, "-89,0,1")] public float MinimumPitchDeg = -65.0f;
    [Export(PropertyHint.Range, "0,89,1")] public float MaximumPitchDeg = 25.0f;
    [Export] public float MouseSensitivity = 0.004f;
    [Export] public string SeatedAnimationName = CharacterVisual.Clips.Sit;
    [Export] public string SeatedPreparationAnimationName = "";
    [Export] public string SeatedIdleAnimationName = CharacterVisual.Clips.IdleHoldingCardsDown;

    [ExportGroup("Top camera")]
    [Export(PropertyHint.Range, "30,100,1")] public float TopFov = 52.0f;
    [Export(PropertyHint.Range, "0.3,2.0,0.01")] public float TopHeight = 0.88f;
    [Export] public float TopPanSensitivity = 0.0012f;
    [Export] public Vector2 TopPanLimit = new(0.34f, 0.34f);

    [ExportGroup("Controller behaviour")]
    [Export] public float LeaveHoldSeconds = 1.0f;
    [Export] public float SeatApproachSpeed = 1.8f;
    [Export] public float MinimumSeatApproachSeconds = 0.25f;
    [Export] public float MaximumSeatApproachSeconds = 2.5f;
    [Export] public float SeatArrivalTolerance = 0.025f;

    [ExportGroup("Action guide")]
    [Export(PropertyHint.Range, "0.30,0.80,0.005")]
    public float InteractionZoneCenterRadius = 0.575f;
    [Export(PropertyHint.Range, "0.06,0.22,0.005")]
    public float ActionZoneRadius = 0.115f;
    [Export(PropertyHint.Range, "0.15,0.50,0.005")]
    public float ConfirmZoneCenterRadius = 0.320f;
    [Export(PropertyHint.Range, "0.03,0.15,0.005")]
    public float ConfirmZoneInnerRadius = 0.070f;
    [Export(PropertyHint.Range, "0.05,0.20,0.005")]
    public float ConfirmZoneOuterRadius = 0.110f;
    [Export(PropertyHint.Range, "0.08,0.24,0.01")]
    public float ConfirmLabelSpanPi = 0.13f;
    [Export(PropertyHint.Range, "0.0005,0.005,0.0001")]
    public float InteractionGuideThickness = 0.0014f;
    [Export(PropertyHint.Range, "48,96,2")] public int ChalkGuideFontSize = 72;
    [Export(PropertyHint.Range, "0.0001,0.0004,0.00001")]
    public float ChalkGuidePixelSize = 0.00019f;

    private int _previewSeat;
    private bool _enablePreviewCamera = true;
    private bool _showFirstPersonHands = true;
    private ulong _previewShapeSignature;
    private bool _previewRebuildQueued;
    private Transform3D _runtimeFirstPersonControllerPose = DefaultFirstPersonControllerPose;
    private Transform3D _runtimeFirstPersonCardsPose = DefaultFirstPersonCardsPose;
    private Transform3D _runtimeFirstPersonCard0Pose;
    private Transform3D _runtimeFirstPersonCard1Pose;

    private static readonly Transform3D DefaultFirstPersonControllerPose = new(
        Basis.Identity, new Vector3(0.045f, -0.05f, -0.22f));
    private static readonly Transform3D DefaultFirstPersonCardsPose = new(
        new Basis(Vector3.Up, Mathf.Pi), Vector3.Zero);

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
        {
            _previewRebuildQueued = true;
            CallDeferred(MethodName.RebuildExperiencePreview);
            CallDeferred(MethodName.UpdateEditorPreview);
        }
        else
        {
            // Controllers are equipped after the editor-only preview is removed. Cache the exact
            // artist-authored marker pose so runtime receives the same position and rotation.
            _runtimeFirstPersonControllerPose =
                FirstPersonHandsPreview?.Transform ?? DefaultFirstPersonControllerPose;
            _runtimeFirstPersonCardsPose =
                FirstPersonCardsPose?.Transform ?? DefaultFirstPersonCardsPose;
            _runtimeFirstPersonCard0Pose =
                FirstPersonCard0Pose?.Transform ?? DefaultIndividualCardPose(0);
            _runtimeFirstPersonCard1Pose =
                FirstPersonCard1Pose?.Transform ?? DefaultIndividualCardPose(1);
            DisableEditorOnlyNodes();
        }
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint())
        {
            QueuePreviewRebuildWhenShapeChanges();
            UpdateExperiencePreviewTransforms();
            UpdateEditorPreview();
        }
    }

    public Transform3D ActionGuideTransformFor(Node3D board, Vector2 facing)
    {
        if (ActionGuideAnchor == null || board == null)
            return Transform3D.Identity;

        var anchorInBoard = board.GlobalTransform.AffineInverse() * ActionGuideAnchor.GlobalTransform;
        // The marker is authored from Seat0. Rotate that whole authored frame to the current seat;
        // guide geometry itself stays canonical, which also makes its hit regions follow exactly.
        return Poker.Rules.PokerTableLayout.ReaderAlignedFrame(
            anchorInBoard, facing, Vector2.Down);
    }

    public Vector2 ActionGuideAimPoint(Node3D board, Vector2 facing, Vector2 boardAim)
    {
        var frame = ActionGuideTransformFor(board, facing);
        var local = frame.AffineInverse() * new Vector3(boardAim.X, 0.0f, boardAim.Y);
        return new Vector2(local.X, local.Z);
    }

    public void ApplyTo(SeatedTableController controller)
    {
        if (controller == null)
            return;

        controller.SeatFov = SeatFov;
        controller.SeatViewOffset = SeatViewOffset;
        controller.RestPitchDeg = RestPitchDeg;
        controller.MaxYawDeg = MaximumYawDeg;
        controller.MinPitchDeg = MinimumPitchDeg;
        controller.MaxPitchDeg = MaximumPitchDeg;
        controller.MouseSensitivity = MouseSensitivity;
        controller.SeatedAnimationName = SeatedAnimationName;
        controller.SeatedPreparationAnimationName = SeatedPreparationAnimationName;
        controller.SeatedIdleAnimationName = SeatedIdleAnimationName;
        controller.TopFov = TopFov;
        controller.TopHeight = TopHeight;
        controller.TopPanSensitivity = TopPanSensitivity;
        controller.TopPanLimit = TopPanLimit;
        controller.LeaveHoldSeconds = LeaveHoldSeconds;
        controller.SeatApproachSpeed = SeatApproachSpeed;
        controller.MinimumSeatApproachSeconds = MinimumSeatApproachSeconds;
        controller.MaximumSeatApproachSeconds = MaximumSeatApproachSeconds;
        controller.SeatArrivalTolerance = SeatArrivalTolerance;
    }

    public void ApplyTo(PokerHand3DView handView)
    {
        if (handView == null)
            return;

        handView.HandPose = ControllerPose;
        handView.CardsInHandPose = CardsPose;
        handView.Card0InHandPose = Card0Pose;
        handView.Card1InHandPose = Card1Pose;
        handView.RestTiltDeg = RestCardsTiltDeg;
        handView.PeekTiltDeg = PeekCardsTiltDeg;
        handView.FanStepDeg = HeldCardsFanStepDeg;
        handView.FanRadius = HeldCardsFanRadius;
        handView.InteractionZoneCenterRadius = InteractionZoneCenterRadius;
        handView.ActionZoneRadius = ActionZoneRadius;
        handView.ConfirmZoneCenterRadius = ConfirmZoneCenterRadius;
        handView.ConfirmZoneInnerRadius = ConfirmZoneInnerRadius;
        handView.ConfirmZoneOuterRadius = ConfirmZoneOuterRadius;
        handView.ConfirmLabelSpanPi = ConfirmLabelSpanPi;
        handView.InteractionGuideThickness = InteractionGuideThickness;
        handView.ChalkGuideFontSize = ChalkGuideFontSize;
        handView.ChalkGuidePixelSize = ChalkGuidePixelSize;
    }

    private void QueuePreviewRefresh()
    {
        if (Engine.IsEditorHint() && IsInsideTree())
        {
            _previewRebuildQueued = true;
            CallDeferred(MethodName.RebuildExperiencePreview);
            CallDeferred(MethodName.UpdateEditorPreview);
        }
    }

    private void QueuePreviewRebuildWhenShapeChanges()
    {
        var signature = PreviewShapeSignature();
        if (_previewShapeSignature == signature || _previewRebuildQueued)
            return;

        _previewShapeSignature = signature;
        QueuePreviewRefresh();
    }

    private ulong PreviewShapeSignature()
    {
        var hash = new HashCode();
        hash.Add(PreviewCardWidth);
        hash.Add(PreviewCardLength);
        hash.Add(PreviewCommunityCardScale);
        hash.Add(PreviewCardGap);
        hash.Add(RestCardsTiltDeg);
        hash.Add(PeekCardsTiltDeg);
        hash.Add(HeldCardsFanStepDeg);
        hash.Add(HeldCardsFanRadius);
        hash.Add(InteractionZoneCenterRadius);
        hash.Add(ActionZoneRadius);
        hash.Add(ConfirmZoneCenterRadius);
        hash.Add(ConfirmZoneInnerRadius);
        hash.Add(ConfirmZoneOuterRadius);
        hash.Add(ConfirmLabelSpanPi);
        hash.Add(InteractionGuideThickness);
        hash.Add(ChalkGuideFontSize);
        hash.Add(ChalkGuidePixelSize);
        return unchecked((ulong)hash.ToHashCode());
    }

    private void UpdateEditorPreview()
    {
        if (!Engine.IsEditorHint() || !IsInsideTree())
            return;

        if (PreviewCamera != null)
        {
            PreviewCamera.Fov = SeatFov;
            PreviewCamera.Current = EnablePreviewCamera;
        }

        if (FirstPersonHandsPreview != null)
            FirstPersonHandsPreview.Visible = ShowFirstPersonHands;

        if (Seats == null || PreviewSeat >= Seats.GetChildCount()
            || Seats.GetChild(PreviewSeat) is not Node3D seat)
            return;

        var eye = seat.GetNodeOrNull<Node3D>("SeatView") ?? seat;
        var eyeTransform = eye.GlobalTransform;
        var cameraTransform = new Transform3D(
            eyeTransform.Basis.Orthonormalized(),
            eyeTransform.Origin + eyeTransform.Basis.Orthonormalized() * SeatViewOffset);
        cameraTransform.Basis *= Basis.FromEuler(new Vector3(
            Mathf.DegToRad(RestPitchDeg), 0.0f, 0.0f));

        if (PreviewCamera != null)
            PreviewCamera.GlobalTransform = cameraTransform;
        UpdateFirstPersonPreview(cameraTransform);
    }

    private void UpdateFirstPersonPreview(Transform3D cameraTransform)
    {
        if (FirstPersonHandsPreview == null)
            return;

        var animator = FirstPersonHandsPreview.FindChild("AnimationPlayer", true, false)
            as AnimationPlayer;
        if (animator?.HasAnimation(CharacterVisual.Clips.IdleHoldingCardsDown) == true)
        {
            animator.Play(CharacterVisual.Clips.IdleHoldingCardsDown);
            animator.Seek(1.0, update: true);
        }

        var skeleton = FirstPersonHandsPreview.FindChild("Skeleton3D", true, false) as Skeleton3D;
        var grip = FirstPersonHandsPreview.GetNodeOrNull<Node3D>("CardGrip");
        if (skeleton != null && grip != null)
            CharacterVisual.UpdateAuthoredCardGrip(skeleton, grip);

        UpdateFirstPersonCardsPreview(grip?.GlobalTransform ?? FirstPersonHandsPreview.GlobalTransform);
    }

    private void DisableEditorOnlyNodes()
    {
        // Exported C# node fields are exposed through generated property getters. Leaving one of
        // those fields pointing at a node after QueueFree() makes the Inspector/remote debugger try
        // to convert a disposed GodotObject into a Variant, which throws ObjectDisposedException.
        // Cache the nodes, clear every exported reference synchronously, then schedule disposal.
        var previewCamera = PreviewCamera;
        var handsPreview = FirstPersonHandsPreview;
        var validCamera = IsInstanceValid(previewCamera);
        var validHands = IsInstanceValid(handsPreview);
        var handsBelongToCamera = validCamera && validHands
            && previewCamera.IsAncestorOf(handsPreview);

        PreviewCamera = null;
        FirstPersonHandsPreview = null;
        FirstPersonCardsPose = null;
        FirstPersonCard0Pose = null;
        FirstPersonCard1Pose = null;

        if (validHands)
        {
            handsPreview.Visible = false;
            handsPreview.ProcessMode = ProcessModeEnum.Disabled;
            if (!handsBelongToCamera)
                handsPreview.QueueFree();
        }

        if (validCamera)
        {
            previewCamera.Current = false;
            previewCamera.QueueFree();
        }
    }

    private Transform3D ControllerPose => Engine.IsEditorHint()
        ? FirstPersonHandsPreview?.Transform ?? DefaultFirstPersonControllerPose
        : _runtimeFirstPersonControllerPose;

    private Transform3D CardsPose => Engine.IsEditorHint()
        ? FirstPersonCardsPose?.Transform ?? DefaultFirstPersonCardsPose
        : _runtimeFirstPersonCardsPose;

    private Transform3D Card0Pose => Engine.IsEditorHint()
        ? FirstPersonCard0Pose?.Transform ?? DefaultIndividualCardPose(0)
        : _runtimeFirstPersonCard0Pose;

    private Transform3D Card1Pose => Engine.IsEditorHint()
        ? FirstPersonCard1Pose?.Transform ?? DefaultIndividualCardPose(1)
        : _runtimeFirstPersonCard1Pose;

    private Transform3D DefaultIndividualCardPose(int index) => HandFan.SlotTransform(
        index, HandFan.NaturalCentre(Poker.Rules.PokerDeal.HoleCardCount), false,
        new HandFanSpec(HeldCardsFanStepDeg, HeldCardsFanRadius, 0.0f,
            RestCardsTiltDeg, HandFan.LongAxisUpFromMinusZ));
}
