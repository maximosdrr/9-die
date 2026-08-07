using Godot;

[GlobalClass]
public partial class TurnRuler : Node
{
    public enum Actions
    {
        /// <summary>
        /// No decision. Deliberately first, so it is what `default(Actions)` yields: a ruler that
        /// forgets to implement Rule then does nothing instead of silently picking whichever
        /// action happened to be declared first.
        /// </summary>
        None,

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
        GD.PushError($"{GetType().Name} não implementa Rule(); o turno não será resolvido.");
        return Actions.None;
    }
}
