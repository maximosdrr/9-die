using Godot;
using Godot.Collections;

[GlobalClass]
public partial class TurnRuler : Node
{
    public enum Actions
    {
        CallFoul,
        CallCueBallReplacement,
        CallGoldenBallReplacement,
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

    public virtual Actions Rule(Dictionary context)
    {
        return default;
    }
}
