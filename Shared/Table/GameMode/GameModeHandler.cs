using Godot;

[GlobalClass]
public partial class GameModeHandler : Node
{
    [Export] public GameMode CurrentGameMode;

    public void Setup(TableGame tableGame)
    {
        CurrentGameMode.TurnResolver.Setup(tableGame);
    }
}
