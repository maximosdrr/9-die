using Godot;
using Godot.Collections;

[GlobalClass]
public partial class Player : CharacterBody3D
{
    [Export] public float Gravity = 12f;
    [Export] public float Speed = 3f;

    [ExportGroup("Authoritative movement")]
    [Export] public float InputTimeoutSeconds = 0.25f;
    [Export] public int MaxInputPacketsPerSecond = PlayerMovementProtocol.DefaultMaxPacketsPerSecond;
    [Export] public float SnapshotRate = 20.0f;
    [Export] public float MaximumServerYawSpeed = 12.0f;
    [Export] public float RemoteInterpolationSpeed = 14.0f;
    [Export] public float PredictionCorrectionSpeed = 4.0f;
    [Export] public float HardCorrectionDistance = 1.5f;

    public int Id = 1;
    [Export] public string Nickname = "";
    [ExportGroup("Replaceable character")]
    [Export] public CharacterVisual CharacterVisual;
    [Export] public AnimationPlayer SeatedGestureAnimator;
    [Export] public SeatedGestureFallback SeatedGestureFallback;

    [ExportGroup("Scene References")]
    [Export] public HeadPivot HeadPivot;
    [Export] public PlayerGameHandler GameHandler;
    [Export] public Node3D PlayerModel;
    [Export] public StateMachine StateMachine;
    [Export] public CollisionShape3D BodyCollision;

    [ExportGroup("Local presentation")]
    [Export] public PackedScene LocalPresentationScene;
    [Export] public PackedScene FirstPersonVisualScene;
    [Export] public Node3D FirstPersonRoot;

    public RemoteTransform3D RemoteFps;
    public PlayerHud Hud;
    public TvShareButton TvShareButton;
    public LocalPlayerPresentation LocalPresentation { get; private set; }
    public FirstPersonCharacterVisual FirstPersonVisual { get; private set; }

    public GlobalCamera Camera;
    public TvScreenShare TvScreen;
    public Node LocalPresentationRoot;

    public enum ControllerStatesEnum { Player, Game }

    public ControllerStatesEnum CurrentControlState = ControllerStatesEnum.Player;
    public bool IsInSeatedGameMode { get; private set; }

    public override void _Ready()
    {
        RemoteFps = HeadPivot.CameraMount;
        InitializeNetworkMovement();
        InitializeNetworkProfile();

        if (!IsMultiplayerAuthority())
            return;

        CharacterVisual?.SetLocalFirstPersonBody(true);
        AttachFirstPersonVisual();
        if (Camera != null)
        {
            Camera.CullMask &= ~(1u << 1);
            Camera.CullMask |= 1u << FirstPersonCharacterVisual.RenderLayerBit;
        }

        AttachLocalPresentation();
        TakeControl();
    }

    public override void _ExitTree()
    {
        ClearCameraLookRequests();
        ClearPokerPoseRequests();
        if (!IsInstanceValid(LocalPresentation))
            return;

        LocalPresentation.Detach();
        LocalPresentation.QueueFree();
        LocalPresentation = null;
        Hud = null;
        TvShareButton = null;
    }

    private void AttachFirstPersonVisual()
    {
        if (FirstPersonVisualScene == null || !IsInstanceValid(FirstPersonRoot))
            return;

        FirstPersonVisual = FirstPersonVisualScene.Instantiate<FirstPersonCharacterVisual>();
        FirstPersonVisual.Name = "LocalFirstPersonBody";
        FirstPersonRoot.AddChild(FirstPersonVisual);
        FirstPersonVisual.Visible = !IsInSeatedGameMode;
    }

    private void AttachLocalPresentation()
    {
        if (LocalPresentationScene == null || !IsInstanceValid(LocalPresentationRoot))
            return;

        LocalPresentation = LocalPresentationScene.Instantiate<LocalPlayerPresentation>();
        LocalPresentation.Name = "LocalPlayerPresentation";
        // LocalPresentationRoot is outside this replicated Player subtree. Without explicitly
        // carrying the owner's authority across that boundary, every client-side UI defaults to
        // peer 1 and TvShareButton disables itself for everyone except the host.
        LocalPresentation.SetMultiplayerAuthority(GetMultiplayerAuthority(), recursive: true);
        LocalPresentationRoot.AddChild(LocalPresentation);
        LocalPresentation.Configure(this, TvScreen);

        // Compatibility handles for systems that still access the local player's presentation
        // through Player. They remain null for remote avatars by design.
        Hud = LocalPresentation.Hud;
        TvShareButton = LocalPresentation.TvShareButton;
    }

    public void TakeControl()
    {
        ExitSeatedGameMode();

        if (!IsMultiplayerAuthority())
            return;

        SetPhysicsProcess(true);
        HeadPivot.SetProcessUnhandledInput(true);

        Camera?.TransitionTo(RemoteFps);
        InputFocus.Capture();
        CurrentControlState = ControllerStatesEnum.Player;
    }

    public void GiveControl()
    {
        if (!IsMultiplayerAuthority())
            return;

        SetPhysicsProcess(false);
        Velocity = Vector3.Zero;

        HeadPivot.SetProcessUnhandledInput(false);
        CurrentControlState = ControllerStatesEnum.Game;
    }

    /// <summary>
    /// Freezes a player that is represented by a chair animation instead of the walking body.
    /// This is deliberately separate from GiveControl: pool still needs the character collider
    /// while aiming, whereas domino players must not fight the chair or one another through
    /// MoveAndSlide while seated.
    /// </summary>
    public void EnterSeatedGameMode()
    {
        IsInSeatedGameMode = true;
        SetPhysicsProcess(false);
        Velocity = Vector3.Zero;

        BodyCollision ??= GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        BodyCollision?.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
    }

    /// <summary>Restores the walking collider when the seated controller is released.</summary>
    public void ExitSeatedGameMode()
    {
        if (_pokerCardsRaised)
        {
            if (IsMultiplayerAuthority())
                SetPokerCardLook(false);
            else
                ApplyPokerCardLook(false);
        }
        IsInSeatedGameMode = false;
        if (IsInstanceValid(FirstPersonVisual))
            FirstPersonVisual.Visible = true;
        SetPhysicsProcess(true);
        BodyCollision ??= GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        BodyCollision?.SetDeferred(CollisionShape3D.PropertyName.Disabled, false);
    }

    public void EnterGameControllerMode(string animationName = "")
    {
        // The local camera uses its dedicated forearms, but the body remains alive so every other
        // peer sees this avatar sitting. Geometry layers prevent the local camera from rendering it.
        PlayerModel.Show();
        var metadata = new Dictionary();
        if (!string.IsNullOrWhiteSpace(animationName))
            metadata["animation"] = animationName;

        StateMachine.ChangeState(StatesRef.PlayerStrike, metadata);
    }

    public void ExitGameControllerMode()
    {
        PlayerModel.Show();
        StateMachine.ChangeState(StatesRef.PlayerIdle, new Dictionary());
    }

    public void EnterSeatedAnimation(string transition, string preparation, string idle)
    {
        PlayerModel.Show();

        // Keep the replicated state semantically seated even though CharacterVisual owns the
        // transition queue. Remote peers receive the same state and never fall back to walking.
        var metadata = new Dictionary
        {
            ["animation"] = transition,
            ["preparation"] = preparation,
            ["idle"] = idle,
        };
        StateMachine.ChangeState(StatesRef.PlayerStrike, metadata);
        CharacterVisual?.PlaySequence(transition, preparation, idle);
    }

    public void EnterPokerCardPose()
    {
        CharacterVisual?.PlaySequence(
            CharacterVisual.Clips.SitHoldingCards,
            CharacterVisual.Clips.IdleHoldingCardsDown);
    }

    public void PlayFirstPersonAnimation(string clip, double blend = 0.16) =>
        FirstPersonVisual?.Play(clip, blend);

    public void SetFirstPersonVisualEnabled(bool enabled)
    {
        if (IsInstanceValid(FirstPersonVisual))
        {
            FirstPersonVisual.Visible = enabled;
            FirstPersonVisual.ProcessMode = enabled
                ? ProcessModeEnum.Inherit
                : ProcessModeEnum.Disabled;
        }
    }

    public void PlayFirstPersonSequence(
        string transition, string preparation, string idle, double blend = 0.18) =>
        FirstPersonVisual?.PlaySequence(transition, preparation, idle, blend);

    /// <summary>
    /// Plays a one-shot on the SEATED body — what everyone else sees this player do at the table.
    ///
    /// Deliberately not routed through the state machine: ChangeState is a no-op when the state is
    /// already current, so a second gesture in the same seat would never replay. This drives the
    /// character's own AnimationPlayer directly and leaves the state alone.
    ///
    /// Silently falls back when an optional table gesture has not been authored yet. Poker pickup,
    /// bet, check, reveal and card idles are real clips; fold can still use the fallback.
    /// </summary>
    public float PlaySeatedGesture(string animationName) =>
        PlaySeatedGesture(animationName, GlobalPosition - GlobalBasis.Z);

    /// <summary>
    /// Plays an authored body clip, or a subtle visual-only fallback directed at the table while the
    /// current character still lacks that clip. The fallback never moves the networked player body.
    /// </summary>
    public float PlaySeatedGesture(string animationName, Vector3 tableTarget)
    {
        if (!IsInSeatedGameMode || string.IsNullOrWhiteSpace(animationName))
            return 0.0f;

        // Exported first, so replacing the character only requires reconnecting one field. The
        // fallback keeps every existing Player scene working until that asset arrives.
        var animation = SeatedGestureAnimator
                        ?? GetNodeOrNull<AnimationPlayer>(
                            "FirstPerson/Model3D/PlayerCharacter/AnimationPlayer");
        if (animation != null && animation.HasAnimation(animationName))
        {
            var duration = (float)(animation.GetAnimation(animationName)?.Length ?? 0.0);
            LockPokerPoseForGesture(animationName, duration);
            animation.Play(animationName);
            if (animationName != PokerClips.BodyIdle
                && animation.HasAnimation(PokerClips.BodyIdle))
            {
                animation.Queue(PokerClips.BodyIdle);
            }
            return duration;
        }

        var fallbackDuration = SeatedGestureFallback?.Play(animationName, tableTarget) ?? 0.0f;
        LockPokerPoseForGesture(animationName, fallbackDuration);
        return fallbackDuration;
    }

    /// <summary>
    /// Starts the shared third-person Showdown cutscene and returns the preparation time before the
    /// authored Showdown clip begins. The table presenter adds that exact delay to the physical-card
    /// release seam, keeping fingers and cards synchronized on every peer.
    /// </summary>
    public float PlaySeatedShowdownSequence(
        Vector3 tableTarget,
        float preparationSeconds = PokerClips.ShowdownPreparationSeconds)
    {
        if (!IsInSeatedGameMode)
            return 0.0f;

        var cardsAlreadyRaised = _pokerCardsRaised;
        _pokerCardsRaised = false;
        var preparation = !cardsAlreadyRaised
                          && CharacterVisual?.HasAnimation(CharacterVisual.Clips.IdleSitHoldingCards) == true
            ? Mathf.Max(0.0f, preparationSeconds)
            : 0.0f;

        if (CharacterVisual?.HasAnimation(CharacterVisual.Clips.Showdown) == true)
        {
            var total = (float)CharacterVisual.PlayShowdownSequence(
                cardsAlreadyRaised,
                preparation,
                PokerClips.ShowdownTransitionBlendSeconds);
            LockPokerPoseForGesture(PokerClips.BodyReveal, total);
            return preparation;
        }

        PlaySeatedGesture(PokerClips.BodyReveal, tableTarget);
        return 0.0f;
    }

}
