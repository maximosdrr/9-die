using Godot;

/// <summary>
/// What a seated player sees of their own private holding, and how they ask to act on it.
///
/// This is a presentation seam and nothing more. The contract, which is what keeps it a seam:
///   1. A controller talks to THIS type only. It never touches a Control, a CanvasLayer or a mesh,
///      so swapping the whole presentation is one PackedScene in the inspector.
///   2. A view never calls an RPC, never reads a holding it does not own and never decides whether
///      an action is legal. It renders what it is handed and emits what the player asked for; the
///      controller forwards that to the server, which validates it again from scratch.
///
/// Each game subclasses this to add its own Refresh and its own intent signals — those are the
/// parts that cannot be shared, because they are the game. Everything here is what any seated game
/// needs regardless of what is being held: telling the player something, reporting a refusal,
/// going quiet when the match ends, and asking to leave.
/// </summary>
[GlobalClass]
public partial class SeatedHandView : Node3D
{
	protected Player Player;

	/// <summary>Give up and leave the match. Server-validated like any other intent.</summary>
	[Signal]
	public delegate void SurrenderRequestedEventHandler();

	/// <summary>Greys the whole thing out — the match ended, or this player is out of it.</summary>
	public virtual void SetInteractive(bool interactive) { }

	/// <summary>Tells the view which camera is live, so it can present itself accordingly.</summary>
	public virtual void SetTopViewActive(bool active) { }

	/// <summary>
	/// Says something to the player for a moment. In a mode with no panel this is the only way
	/// anything reaches them in words, so the controller uses it too — for how far along leaving
	/// the table is, for instance.
	/// </summary>
	public virtual void ShowNotice(string text, float seconds = 2.5f) { }

	/// <summary>Reports a server rejection, so the player learns why nothing happened.</summary>
	public virtual void ShowRejection(string reason) { }

	public virtual void Clear() { }
}
