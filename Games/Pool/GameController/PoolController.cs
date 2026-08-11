using Godot;
using Godot.Collections;

[GlobalClass]
public partial class PoolController : GameController
{
    [ExportGroup("Scene References")]
    [Export] public RemoteTransform3D RemoteAim;
    [Export] public AimCameraPivot AimPivot;
    [Export] public Cue Cue;

    public PoolGame PoolGame;
    public Player Player;
    public GlobalCamera Camera;
    private ulong _applyControlRevision;

    public override void Setup(Player parent, TableGame tableGame, GlobalCamera camera)
    {
        Player = parent;
        PoolGame = tableGame as PoolGame;
        Camera = camera;

        AimPivot.Setup(PoolGame, this);
        Cue.Setup(PoolGame, AimPivot);

        SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChange));
        SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
    }

    public override void _ExitTree()
    {
        _applyControlRevision++;
        if (PoolGame == null)
            return;

        SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnChange));
        SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnExtended, new Callable(this, MethodName.OnTurnExtended));
    }

    public override void TakeControl()
    {
        // Any explicit handoff supersedes an older ball-placement continuation that may still be
        // waiting for PlacementFinished (for example a push-out decision resolved meanwhile).
        _applyControlRevision++;

        if (!IsMultiplayerAuthority())
            return;

        if (!IsInstanceValid(PoolGame) || !PoolGame.IsMatchActive || !IsInstanceValid(Player))
        {
            return;
        }

        if (!PoolGame.IsTurnOwner((string)Player.Name))
        {
            GD.PushError("Cannot take control, it's not your turn!");
            return;
        }

        Show();
        Cue?.RestoreAimingPresentation();
        SetProcessUnhandledInput(true);
        SetProcess(true);

        AimPivot.SetProcess(true);
        AimPivot.SetPhysicsProcess(true);
        AimPivot.SetProcessUnhandledInput(true);

        Player.EnterGameControllerMode();

        Camera?.SetGlobalCameraFov(60);
        Camera?.TransitionTo(RemoteAim);
        InputFocus.Capture();
    }

    public override void GiveControl()
    {
        if (!IsMultiplayerAuthority())
            return;

        _applyControlRevision++;
        ReleaseControl();
    }

    private void ReleaseControl()
    {
        Hide();
        Camera?.SetGlobalCameraFov(75);
        SetProcessUnhandledInput(false);
        SetProcess(false);

        AimPivot.SetProcess(false);
        AimPivot.SetPhysicsProcess(false);
        AimPivot.SetProcessUnhandledInput(false);

        Player.ExitGameControllerMode();
    }

    public override async void ApplyControl(string turnOwnerId, Dictionary context)
    {
        var controlRevision = BeginControlRequest();
        if (!IsInsideTree() || !IsInstanceValid(PoolGame) || !IsInstanceValid(Player))
            return;

        if (turnOwnerId == (string)Player.Name)
        {
            CanTakeControl = true;
            Player.GiveControl();
            if (context.TryGetValue("push_out_choice_pending", out var pendingChoice)
                && pendingChoice.AsBool())
            {
                ReleaseControl();
            }
            else if (!context.ContainsKey("ball_replacement"))
            {
                TakeControl();
            }
            else
            {
                // A newly equipped controller starts visible. Hide the cue and disable aiming while
                // ball placement owns the camera and input, including before the opening break.
                ReleaseControl();
                await ToSignal(PoolGame.BallPlacementManager, BallPlacementManager.SignalName.PlacementFinished);
                if (!IsCurrentControlRequest(controlRevision)
                    || !IsInsideTree() || !IsInstanceValid(PoolGame) || !PoolGame.IsMatchActive
                    || !IsInstanceValid(Player)
                    || !PoolGame.IsTurnOwner((string)Player.Name))
                    return;

                TakeControl();
            }
        }
        else
        {
            CanTakeControl = false;
            ReleaseControl();
            Player.TakeControl();
        }
    }

    internal ulong BeginControlRequest() => ++_applyControlRevision;

    internal bool IsCurrentControlRequest(ulong revision) =>
        revision == _applyControlRevision;

    private void OnTurnChange(string nextPlayerName, Dictionary context)
    {
        ApplyControl(nextPlayerName, context);
    }

    private void OnTurnExtended(Dictionary context)
    {
        if (!IsMultiplayerAuthority() || !IsInstanceValid(PoolGame)
            || !PoolGame.IsMatchActive || !IsInstanceValid(Player)
            || !PoolGame.IsTurnOwner((string)Player.Name))
            return;

        if (context.ContainsKey("ball_replacement"))
        {
            ApplyControl((string)Player.Name, context);
            return;
        }

        if (context.TryGetValue("push_out_choice_resolved", out var resolved) && resolved.AsBool())
            TakeControl();
    }
}
