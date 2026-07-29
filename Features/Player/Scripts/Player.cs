using Godot;

[GlobalClass]
public partial class Player : CharacterBody3D
{
    public float Gravity = 12f;
    public float Speed = 3f;
    public int Id = 1;

    public HeadPivot HeadPivot;
    public RemoteTransform3D RemoteFps;
    public Toggleable PlayerToggleable;
    public PlayerGameHandler GameHandler;
    public Node3D PlayerModel;
    public StateMachine StateMachine;

    public enum ControllerStatesEnum { Player, Game }

    public ControllerStatesEnum CurrentControlState = ControllerStatesEnum.Player;

    public override void _Ready()
    {
        HeadPivot = GetNode<HeadPivot>("FirstPerson/HeadPivot");
        RemoteFps = GetNode<RemoteTransform3D>("FirstPerson/HeadPivot/RemoteFPS");
        PlayerToggleable = GetNode<Toggleable>("Scripts/PlayerToggleable");
        GameHandler = GetNode<PlayerGameHandler>("Scripts/PlayerGameHandler");
        PlayerModel = GetNode<Node3D>("FirstPerson/Model3D");
        StateMachine = GetNode<StateMachine>("StateMachine");

        if (!IsMultiplayerAuthority())
            return;

        TakeControl();
    }

    public void TakeControl()
    {
        if (!IsMultiplayerAuthority())
            return;

        SetPhysicsProcess(true);
        HeadPivot.SetProcessUnhandledInput(true);

        Global.Instance.Camera.TransitionTo(RemoteFps);
        Input.MouseMode = Input.MouseModeEnum.Captured;
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
