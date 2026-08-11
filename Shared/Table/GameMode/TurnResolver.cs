using Godot;
using Godot.Collections;

[GlobalClass]
public partial class TurnResolver : Node
{
    public virtual void HandleTurnExtensionContext(Dictionary context) { }

    public virtual void HandleNewTurnContext(Dictionary context) { }

    public virtual void Setup(TableGame tableGame) { }

    public virtual Dictionary BuildHandoffContext(string outgoingPlayerId) => new Dictionary();

    /// <summary>
    /// Builds the state sent while an existing seat changes network identity. Most modes can use
    /// their ordinary handoff snapshot; modes with stamped turns may distinguish a reconnect from
    /// a permanent departure without overloading the removal semantics.
    /// </summary>
    public virtual Dictionary BuildReclaimContext(string outgoingPlayerId) =>
        BuildHandoffContext(outgoingPlayerId);

    /// <summary>Stops mode-specific work when a match ends outside the normal turn path.</summary>
    public virtual void HandleMatchEnded() { }
}
