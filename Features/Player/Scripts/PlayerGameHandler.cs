using Godot;

[GlobalClass]
public partial class PlayerGameHandler : Node
{
    [Export] public Node3D ContextSlot;
    [Export] public Player Player;

    public GameController CurrentController = null;

    [Signal]
    public delegate void ControllerEquippedEventHandler(TableGame tableGame);

    [Signal]
    public delegate void ControllerUnequippedEventHandler();

    public void EquipGameController(PackedScene controllerScene, TableGame tableGame, GlobalCamera camera)
    {
        UnequipCurrentController();

        var controllerInstance = controllerScene.Instantiate();
        controllerInstance.Name = "ActiveController";
        controllerInstance.SetMultiplayerAuthority(Player.Id);

        CurrentController = (GameController)controllerInstance;

        CurrentController.Hide();

        if (!IsMultiplayerAuthority())
            ContextSlot.Hide();

        ContextSlot.AddChild(CurrentController);
        CurrentController.Setup(Player, tableGame, camera);

        EmitSignal(SignalName.ControllerEquipped, tableGame);
    }

    public void UnequipCurrentController()
    {
        if (CurrentController == null)
            return;

        var currentControllerParent = CurrentController.GetParent();

        if (currentControllerParent != null)
            currentControllerParent.RemoveChild(CurrentController);

        CurrentController.QueueFree();
        CurrentController = null;

        EmitSignal(SignalName.ControllerUnequipped);
    }
}
