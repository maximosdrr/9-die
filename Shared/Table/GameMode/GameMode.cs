using Godot;

[GlobalClass]
public partial class GameMode : Node
{
    public TurnRuler TurnRuler;
    public TurnResolver TurnResolver;

    public override void _Ready()
    {
        foreach (var node in GetChildren())
        {
            if (node is TurnRuler turnRuler)
                TurnRuler = turnRuler;
            else if (node is TurnResolver turnResolver)
                TurnResolver = turnResolver;
            else
                GD.PushError("Game Mode should only have children of type TurnRuler or TurnResolver");
        }
    }
}
