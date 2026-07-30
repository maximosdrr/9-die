using Godot;
using Godot.Collections;

[GlobalClass]
public partial class Player : CharacterBody3D
{
    [Export] public float Gravity = 12f;
    [Export] public float Speed = 3f;
    public int Id = 1;
    [Export] public string Nickname = "";

    public HeadPivot HeadPivot;
    public RemoteTransform3D RemoteFps;
    public PlayerGameHandler GameHandler;
    public Node3D PlayerModel;
    public StateMachine StateMachine;
    public PlayerHud Hud;
    public TvShareButton TvShareButton;

    public GlobalCamera Camera;
    public TvScreenShare TvScreen;

    public enum ControllerStatesEnum { Player, Game }

    public ControllerStatesEnum CurrentControlState = ControllerStatesEnum.Player;

    public override void _Ready()
    {
        HeadPivot = GetNode<HeadPivot>("FirstPerson/HeadPivot");
        RemoteFps = HeadPivot.CameraMount;
        GameHandler = GetNode<PlayerGameHandler>("Scripts/PlayerGameHandler");
        PlayerModel = GetNode<Node3D>("FirstPerson/Model3D");
        StateMachine = GetNode<StateMachine>("StateMachine");
        Hud = GetNode<PlayerHud>("UI/PlayerHud");
        TvShareButton = GetNode<TvShareButton>("UI/TvShareButton");

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

    public void EnterGameControllerMode()
    {
        PlayerModel.Hide();
        StateMachine.ChangeState(StatesRef.PlayerStrike, new Dictionary());
    }

    public void ExitGameControllerMode()
    {
        PlayerModel.Show();
        StateMachine.ChangeState(StatesRef.PlayerIdle, new Dictionary());
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
