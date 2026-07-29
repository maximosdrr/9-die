using Godot;
using Godot.Collections;

[GlobalClass]
public partial class GameModeHandler : Node
{
    [Export] public GameMode CurrentGameMode;

    public Array<GameMode> Modes = new();

    public void Setup(TableGame tableGame)
    {
        foreach (var node in GetChildren())
        {
            if (node is GameMode gameMode)
            {
                gameMode.TurnResolver.Setup(tableGame);
                Modes.Add(gameMode);
            }
        }
    }

    public void Switch(GameMode gameMode)
    {
        if (gameMode != CurrentGameMode)
            CurrentGameMode = gameMode;
    }
}
