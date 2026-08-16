using Godot;

/// <summary>
/// Stable integration surface around the Blender character.
///
/// Imported node names stay inside this component; gameplay only asks for clips, look direction
/// and the authored card grip. The five body meshes remain separate in the GLB.
/// </summary>
[Tool]
[GlobalClass]
public partial class CharacterVisual : Node3D
{
    public const float DefaultCharacterScale = 0.64f;
    private const string CardHandBone = "CC_Base_L_Hand";
    private const string AuthoredCardGripMarkerName = "CardGripMarker";

    public static class Clips
    {
        public const string Idle = "Idle";
        public const string Walk = "Walk";
        public const string Sit = "Sit";
        public const string IdleSit = "IdleSit";
        public const string SitHoldingCards = "SitHoldingCards";
        public const string PickCards = "PickCards";
        public const string IdleSitHoldingCards = "IdleSitHoldingCards";
        public const string IdleHoldingCardsDown = "IdleHoldingCardsDown";
        public const string PokerPass = "PokerPass";
        public const string PokerBet = "PokerBet";
        public const string Showdown = "Showdown";
    }

    [Export] public Node3D RigRoot;
    [Export] public Skeleton3D Skeleton;
    [Export] public AnimationPlayer Animator;
    [Export] public Node3D CardGrip;
    [Export] public Node3D AuthoredCardGripMarker;
    [Export] public CameraNeckModifier NeckModifier;

    private float _characterScale = DefaultCharacterScale;
    private CardSequenceStage _cardSequenceStage;
    private double _cardSequenceRemaining;
    private double _cardSequenceLookSeconds;
    private double _cardSequenceBlend = 0.18;

    private enum CardSequenceStage
    {
        None,
        PickingUp,
        Looking,
        ShowdownPreparing,
        ShowdownPlaying,
    }

    [ExportGroup("Visual size")]
    /// <summary>
    /// Uniform size of the third-person character. Editable on CharacterVisual.tscn; seat previews
    /// use the same scene, so chair fit can be judged before running the game.
    /// </summary>
    [Export(PropertyHint.Range, "0.45,0.85,0.01")]
    public float CharacterScale
    {
        get => _characterScale;
        set
        {
            _characterScale = Mathf.Clamp(value, 0.45f, 0.85f);
            ApplyCharacterScale();
        }
    }

    [ExportGroup("Camera-driven neck")]
    [Export] public float MaximumNeckYawDegrees = 70.0f;
    [Export] public float MinimumNeckPitchDegrees = -45.0f;
    [Export] public float MaximumNeckPitchDegrees = 40.0f;

    public override void _Ready()
    {
        ApplyCharacterScale();

        // Running late provides a useful fallback before the skeleton's deferred update. The
        // authoritative attachment refresh is SkeletonUpdated below, after animation and every
        // SkeletonModifier3D have produced the pose that is actually rendered.
        ProcessPriority = 100;

        if (Animator != null)
        {
            SetLoop(Clips.Idle);
            SetLoop(Clips.Walk);
            SetLoop(Clips.IdleSit);
            SetLoop(Clips.IdleSitHoldingCards);
            SetLoop(Clips.IdleHoldingCardsDown);
        }

        if (Skeleton != null)
            Skeleton.SkeletonUpdated += OnSkeletonUpdated;

        UpdateCardGrip();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Skeleton))
            Skeleton.SkeletonUpdated -= OnSkeletonUpdated;
    }

    private void ApplyCharacterScale()
    {
        if (RigRoot == null)
            return;

        // Preserve the import's authored orientation while changing only its uniform magnitude.
        var current = RigRoot.Scale;
        var sign = new Vector3(
            current.X < 0.0f ? -1.0f : 1.0f,
            current.Y < 0.0f ? -1.0f : 1.0f,
            current.Z < 0.0f ? -1.0f : 1.0f);
        RigRoot.Scale = sign * CharacterScale;
    }

    public override void _Process(double delta)
    {
        AdvanceCardSequence(delta);
        UpdateCardGrip();
    }

    public void SetCameraLook(float yawRadians, float pitchRadians)
    {
        NeckModifier?.SetLook(new Vector2(
            Mathf.Clamp(yawRadians,
                -Mathf.DegToRad(MaximumNeckYawDegrees),
                Mathf.DegToRad(MaximumNeckYawDegrees)),
            Mathf.Clamp(pitchRadians,
                Mathf.DegToRad(MinimumNeckPitchDegrees),
                Mathf.DegToRad(MaximumNeckPitchDegrees))));
    }

    public void ResetCameraLook() => NeckModifier?.ResetLook();

    public bool HasAnimation(string clip) =>
        Animator != null && !string.IsNullOrWhiteSpace(clip) && Animator.HasAnimation(clip);

    public void Play(string clip, double blend = 0.16, float speed = 1.0f)
    {
        _cardSequenceStage = CardSequenceStage.None;
        PlayInternal(clip, blend, speed);
    }

    private void PlayInternal(
        string clip, double blend = 0.16, float speed = 1.0f, bool restart = false)
    {
        if (!HasAnimation(clip))
            return;
        if (!restart && Animator.CurrentAnimation == clip && Animator.IsPlaying())
            return;

        Animator.Play(clip, blend, speed);
    }

    /// <summary>
    /// Runs the authored opening beat without queuing behind a looping look idle:
    /// PickCards -> IdleSitHoldingCards -> IdleHoldingCardsDown.
    /// </summary>
    public double PlayCardPickupSequence(double lookSeconds, double blend = 0.18)
    {
        _cardSequenceLookSeconds = Mathf.Max(lookSeconds, 0.0);
        if (!HasAnimation(Clips.PickCards))
        {
            Play(Clips.IdleHoldingCardsDown, blend);
            return 0.0;
        }

        var pickUp = Animator.GetAnimation(Clips.PickCards)?.Length ?? 0.0;
        _cardSequenceStage = CardSequenceStage.PickingUp;
        _cardSequenceRemaining = Mathf.Max(pickUp, 0.01);
        PlayInternal(Clips.PickCards, blend, restart: true);
        return pickUp;
    }

    /// <summary>
    /// Runs the public reveal as a small authored cutscene. A player whose cards are already raised
    /// enters Showdown directly; a player resting on the table first blends into the raised holding
    /// pose. The explicit timer is required because IdleSitHoldingCards is a loop and therefore
    /// cannot be followed with AnimationPlayer.Queue.
    /// </summary>
    public double PlayShowdownSequence(
        bool cardsAlreadyRaised,
        double preparationSeconds,
        double blend = 0.22)
    {
        if (!HasAnimation(Clips.Showdown))
        {
            Play(Clips.IdleHoldingCardsDown, blend);
            return 0.0;
        }

        _cardSequenceBlend = Mathf.Max(blend, 0.0);
        var revealSeconds = Animator.GetAnimation(Clips.Showdown)?.Length ?? 0.0;
        var canPrepare = !cardsAlreadyRaised
                         && preparationSeconds > 0.0
                         && HasAnimation(Clips.IdleSitHoldingCards);
        if (canPrepare)
        {
            _cardSequenceStage = CardSequenceStage.ShowdownPreparing;
            _cardSequenceRemaining = preparationSeconds;
            PlayInternal(Clips.IdleSitHoldingCards, _cardSequenceBlend, restart: true);
            return preparationSeconds + revealSeconds;
        }

        _cardSequenceStage = CardSequenceStage.ShowdownPlaying;
        _cardSequenceRemaining = Mathf.Max(revealSeconds, 0.01);
        PlayInternal(Clips.Showdown, _cardSequenceBlend, restart: true);
        return revealSeconds;
    }

    private void AdvanceCardSequence(double delta)
    {
        if (_cardSequenceStage == CardSequenceStage.None)
            return;

        _cardSequenceRemaining -= delta;
        if (_cardSequenceRemaining > 0.0)
            return;

        if (_cardSequenceStage == CardSequenceStage.PickingUp)
        {
            _cardSequenceStage = CardSequenceStage.Looking;
            _cardSequenceRemaining = Mathf.Max(_cardSequenceLookSeconds, 0.01);
            PlayInternal(Clips.IdleSitHoldingCards, restart: true);
            return;
        }

        if (_cardSequenceStage == CardSequenceStage.ShowdownPreparing)
        {
            _cardSequenceStage = CardSequenceStage.ShowdownPlaying;
            _cardSequenceRemaining = Mathf.Max(
                Animator.GetAnimation(Clips.Showdown)?.Length ?? 0.0, 0.01);
            PlayInternal(Clips.Showdown, _cardSequenceBlend, restart: true);
            return;
        }

        if (_cardSequenceStage == CardSequenceStage.ShowdownPlaying)
        {
            _cardSequenceStage = CardSequenceStage.None;
            PlayInternal(Clips.IdleHoldingCardsDown, _cardSequenceBlend, restart: true);
            return;
        }

        _cardSequenceStage = CardSequenceStage.None;
        PlayInternal(Clips.IdleHoldingCardsDown, restart: true);
    }

    public void PlaySequence(string transition, string idle, double blend = 0.18)
    {
        PlaySequence(transition, "", idle, blend);
    }

    public void PlaySequence(
        string transition, string preparation, string idle, double blend = 0.18)
    {
        _cardSequenceStage = CardSequenceStage.None;
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

    public void SetLocalFirstPersonBody(bool localOwner)
    {
        SetRenderLayer(this, localOwner ? 2u : 1u);
    }

    /// <summary>
    /// The artist authored the card fan relative to the left hand in Blender. Keeping that authored
    /// offset here avoids making gameplay depend on glTF's internal centimetre-scale attachment.
    /// The public marker is scale-free because PokerCard geometry is authored directly in metres.
    /// </summary>
    private void UpdateCardGrip()
    {
        UpdateAuthoredCardGrip(Skeleton, CardGrip, AuthoredCardGripMarker);
    }

    private void OnSkeletonUpdated() => UpdateCardGrip();

    public static void UpdateAuthoredCardGrip(
        Skeleton3D skeleton,
        Node3D cardGrip,
        Node3D authoredMarker = null)
    {
        if (skeleton == null || cardGrip == null)
            return;

        var hand = skeleton.FindBone(CardHandBone);
        if (hand < 0)
            return;

        // New Blender exports contain an Empty positioned exactly at the current red placeholder.
        // Godot imports it below a BoneAttachment3D; its local transform is the authored offset from
        // the hand. Compose that offset ourselves because BoneAttachment3D's displayed global
        // transform includes an import conversion that must not leak into the gameplay marker.
        authoredMarker ??= skeleton.GetParent()?.FindChild(
            AuthoredCardGripMarkerName, recursive: true, owned: false) as Node3D;
        if (authoredMarker != null)
        {
            var markerTransform = skeleton.GlobalTransform
                                  * skeleton.GetBoneGlobalPose(hand)
                                  * authoredMarker.Transform;
            cardGrip.GlobalTransform = new Transform3D(
                markerTransform.Basis.Orthonormalized(), markerTransform.Origin);
            return;
        }

        // Compatibility fallback for older character assets that predate CardGripMarker.
        var authored = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(hand);
        cardGrip.GlobalTransform = new Transform3D(
            authored.Basis.Orthonormalized(), authored.Origin);
    }

    private static void SetRenderLayer(Node node, uint layer)
    {
        if (node is VisualInstance3D visual)
            visual.Layers = layer;

        foreach (var child in node.GetChildren())
            SetRenderLayer(child, layer);
    }

    private void SetLoop(string clip)
    {
        var animation = Animator.GetAnimation(clip);
        if (animation != null)
            animation.LoopMode = Animation.LoopModeEnum.Linear;
    }
}
