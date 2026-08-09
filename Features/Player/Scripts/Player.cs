using Godot;
using Godot.Collections;

[GlobalClass]
public partial class Player : CharacterBody3D
{
    [Export] public float Gravity = 12f;
    [Export] public float Speed = 3f;
    public int Id = 1;
    [Export] public string Nickname = "";
    [ExportGroup("Replaceable character")]
    [Export] public AnimationPlayer SeatedGestureAnimator;

    public HeadPivot HeadPivot;
    public RemoteTransform3D RemoteFps;
    public PlayerGameHandler GameHandler;
    public Node3D PlayerModel;
    public StateMachine StateMachine;
    public PlayerHud Hud;
    public TvShareButton TvShareButton;
    public CollisionShape3D BodyCollision;

    public GlobalCamera Camera;
    public TvScreenShare TvScreen;

    public enum ControllerStatesEnum { Player, Game }

    public ControllerStatesEnum CurrentControlState = ControllerStatesEnum.Player;
    public bool IsInSeatedGameMode { get; private set; }

    public override void _Ready()
    {
        HeadPivot = GetNode<HeadPivot>("FirstPerson/HeadPivot");
        RemoteFps = HeadPivot.CameraMount;
        GameHandler = GetNode<PlayerGameHandler>("Scripts/PlayerGameHandler");
        PlayerModel = GetNode<Node3D>("FirstPerson/Model3D");
        StateMachine = GetNode<StateMachine>("StateMachine");
        Hud = GetNode<PlayerHud>("UI/PlayerHud");
        TvShareButton = GetNode<TvShareButton>("UI/TvShareButton");
        BodyCollision = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");

        // Godot calls _Ready() bottom-up (children before parents), so PlayerHud._Ready()
        // would run before this point and see GameHandler as null if it tried to wire
        // itself. Player explicitly initializes it here, after GameHandler is assigned.
        Hud.Initialize(this);
        TvShareButton.Initialize(TvScreen);

        if (!IsMultiplayerAuthority())
            return;

        var localNickname = Global.Instance.LocalNickname;
        Nickname = string.IsNullOrWhiteSpace(localNickname) ? $"Player {Id}" : localNickname.Trim();

        TakeControl();
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
    public void PlaySeatedGesture(string animationName)
    {
        if (!IsInSeatedGameMode || string.IsNullOrWhiteSpace(animationName))
            return;

        // Exported first, so replacing the character only requires reconnecting one field. The
        // fallback keeps every existing Player scene working until that asset arrives.
        var animation = SeatedGestureAnimator
                        ?? GetNodeOrNull<AnimationPlayer>("FirstPerson/Model3D/AnimationPlayer");
        if (animation != null && animation.HasAnimation(animationName))
            animation.Play(animationName);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsMultiplayerAuthority())
            return;

        var velocity = Velocity;

        if (!IsOnFloor())
            velocity.Y -= Gravity * (float)delta;
        else
            velocity.Y = 0;

        var inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");
        var direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

        if (direction != Vector3.Zero)
        {
            velocity.X = direction.X * Speed;
            velocity.Z = direction.Z * Speed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(velocity.X, 0, Speed);
            velocity.Z = Mathf.MoveToward(velocity.Z, 0, Speed);
        }

        Velocity = velocity;
        MoveAndSlide();
    }
}
