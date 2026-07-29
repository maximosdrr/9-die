using Godot;

[GlobalClass]
public partial class ControlSwitch : Node
{
    [Export] public Player Player;
    [Export] public Node3D GameContextSlot;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsMultiplayerAuthority())
            return;

        if (!Input.IsActionJustPressed("switch_control"))
            return;

        var gameContextChildren = GameContextSlot.GetChildren();

        if (gameContextChildren.Count == 0)
        {
            GD.PushError("No children in player game controller context yet!");
            return;
        }

        var currentGameController = gameContextChildren[0] as PoolController;

        if (currentGameController == null)
            return;

        if (Player.CurrentControlState == Player.ControllerStatesEnum.Player)
            SwitchToGame(currentGameController);
        else
            SwitchToPlayer(currentGameController);
    }

    private void SwitchToPlayer(PoolController gameController)
    {
        gameController.GiveControl();
        Player.TakeControl();
    }

    private void SwitchToGame(PoolController gameController)
    {
        if (!gameController.CanTakeControl)
        {
            GD.PushError("Cannot take control of this game controller now!");
            return;
        }

        Player.GiveControl();
        gameController.TakeControl();
    }
}
