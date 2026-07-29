using Godot;

[GlobalClass]
public partial class TurnResolver : Node
{
    public virtual void HandleTurnExtensionContext() { }

    public virtual void HandleNewTurnContext() { }

    public virtual void Setup(TableGame tableGame) { }
}
