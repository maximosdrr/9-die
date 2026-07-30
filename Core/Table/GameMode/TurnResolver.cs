using Godot;
using Godot.Collections;

[GlobalClass]
public partial class TurnResolver : Node
{
    public virtual void HandleTurnExtensionContext(Dictionary context) { }

    public virtual void HandleNewTurnContext(Dictionary context) { }

    public virtual void Setup(TableGame tableGame) { }

    public virtual Dictionary BuildHandoffContext(string outgoingPlayerId) => new Dictionary();
}
