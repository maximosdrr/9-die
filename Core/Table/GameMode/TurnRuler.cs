using Godot;

[GlobalClass]
public partial class TurnRuler : Node
{
    public enum Actions
    {
        CallFoul,
        CallCueBallReplacement,
        CallNextTurn,
        ExtendTurn,
        EndGameFatalFoul,
        EndGamePlayerWin,
    }

    public enum RulerType
    {
        GoldenNine,
    }

    public RulerType Type;

    public virtual Actions Rule(TurnContext context)
    {
        return default;
    }
}
