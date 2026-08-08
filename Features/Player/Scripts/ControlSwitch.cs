using Godot;

[GlobalClass]
public partial class ControlSwitch : Node
{
    [Export] public Player Player;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsMultiplayerAuthority())
            return;

        if (!@event.IsActionPressed("switch_control"))
            return;

        var currentGameController = Player.GameHandler.CurrentController;

        if (currentGameController == null)
            return;

        // Some modes own the player continuously and deliberately have nothing to toggle back to.
        if (!currentGameController.AllowsControlSwitch)
            return;

        if (Player.CurrentControlState == Player.ControllerStatesEnum.Player)
            SwitchToGame(currentGameController);
        else
            SwitchToPlayer(currentGameController);

        GetViewport().SetInputAsHandled();
    }

    private void SwitchToPlayer(GameController gameController)
    {
        gameController.GiveControl();
        Player.TakeControl();
    }

    private void SwitchToGame(GameController gameController)
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
