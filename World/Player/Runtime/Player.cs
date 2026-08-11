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
    [Export] public float PredictionYawCorrectionSpeed = 5.0f;
    [Export] public float HardCorrectionDistance = 1.5f;

    public int Id = 1;
    [Export] public string Nickname = "";
    [ExportGroup("Replaceable character")]
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

    public RemoteTransform3D RemoteFps;
    public PlayerHud Hud;
    public TvShareButton TvShareButton;
    public LocalPlayerPresentation LocalPresentation { get; private set; }

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

        AttachLocalPresentation();
        TakeControl();
    }

    public override void _ExitTree()
    {
        if (!IsInstanceValid(LocalPresentation))
            return;

        LocalPresentation.Detach();
        LocalPresentation.QueueFree();
        LocalPresentation = null;
        Hud = null;
        TvShareButton = null;
    }

    private void AttachLocalPresentation()
    {
        if (LocalPresentationScene == null || !IsInstanceValid(LocalPresentationRoot))
            return;

        LocalPresentation = LocalPresentationScene.Instantiate<LocalPlayerPresentation>();
        LocalPresentation.Name = "LocalPlayerPresentation";
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
        IsInSeatedGameMode = false;
        SetPhysicsProcess(true);
        BodyCollision ??= GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
        BodyCollision?.SetDeferred(CollisionShape3D.PropertyName.Disabled, false);
    }

    public void EnterGameControllerMode(string animationName = "")
    {
        PlayerModel.Hide();
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

    /// <summary>
    /// Plays a one-shot on the SEATED body — what everyone else sees this player do at the table.
    ///
    /// Deliberately not routed through the state machine: ChangeState is a no-op when the state is
    /// already current, so a second gesture in the same seat would never replay. This drives the
    /// character's own AnimationPlayer directly and leaves the state alone.
    ///
    /// Silently does nothing when the clip does not exist, which is every table gesture today — the
    /// rig carries only Idle and Walk. The call sites are what matter now; the clips drop in later
    /// with no change here.
    /// </summary>
    public void PlaySeatedGesture(string animationName) =>
        PlaySeatedGesture(animationName, GlobalPosition - GlobalBasis.Z);

    /// <summary>
    /// Plays an authored body clip, or a subtle visual-only fallback directed at the table while the
    /// current character still lacks that clip. The fallback never moves the networked player body.
    /// </summary>
    public void PlaySeatedGesture(string animationName, Vector3 tableTarget)
    {
        if (!IsInSeatedGameMode || string.IsNullOrWhiteSpace(animationName))
            return;

        // Exported first, so replacing the character only requires reconnecting one field. The
        // fallback keeps every existing Player scene working until that asset arrives.
        var animation = SeatedGestureAnimator
                        ?? GetNodeOrNull<AnimationPlayer>("FirstPerson/Model3D/AnimationPlayer");
        if (animation != null && animation.HasAnimation(animationName))
        {
            animation.Play(animationName);
            if (animationName != PokerClips.BodyIdle && animation.HasAnimation(PokerClips.BodyIdle))
                animation.Queue(PokerClips.BodyIdle);
            return;
        }

        SeatedGestureFallback?.Play(animationName, tableTarget);
    }

}
