using Godot;

[GlobalClass]
public partial class PlayerGameHandler : Node
{
    [Export] public Node3D ContextSlot;
    [Export] public Player Player;

    public GameController CurrentController = null;
    public TableGame CurrentTableGame { get; private set; }

    [Signal]
    public delegate void ControllerEquippedEventHandler(TableGame tableGame);

    [Signal]
    public delegate void ControllerUnequippedEventHandler();

    public void EquipGameController(PackedScene controllerScene, TableGame tableGame, GlobalCamera camera)
    {
        UnequipCurrentController();
        CurrentTableGame = tableGame;

        var controllerInstance = controllerScene.Instantiate();
        controllerInstance.Name = "ActiveController";
        controllerInstance.SetMultiplayerAuthority(Player.Id);

        CurrentController = (GameController)controllerInstance;

        CurrentController.Hide();

        if (!IsMultiplayerAuthority())
            ContextSlot.Hide();

        ContextSlot.AddChild(CurrentController);
        CurrentController.Setup(Player, tableGame, camera);
        CurrentController.SetLocalPresentationActive(CurrentController.IsMultiplayerAuthority());

        EmitSignal(SignalName.ControllerEquipped, tableGame);
    }

    public void UnequipCurrentController()
    {
        if (!IsInstanceValid(CurrentController))
        {
            CurrentController = null;
            CurrentTableGame = null;
            return;
        }

        var currentControllerParent = CurrentController.GetParent();

        if (currentControllerParent != null)
            currentControllerParent.RemoveChild(CurrentController);

        CurrentController.QueueFree();
        CurrentController = null;
        CurrentTableGame = null;

        EmitSignal(SignalName.ControllerUnequipped);
    }
}
