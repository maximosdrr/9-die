using System.Collections.Generic;
using Domino.Rules;
using Godot;

/// <summary>
/// How a player sees and plays their own tiles. This is a presentation seam and nothing more.
///
/// The contract, which is what keeps it a seam:
///   1. DominoController talks to THIS type only. It never touches a Control, a CanvasLayer or a
///      mesh, so swapping the whole presentation is one PackedScene in the inspector.
///   2. A view never calls an RPC, never reads a hand it does not own and never decides whether a
///      move is legal. It renders what it is handed and emits what the player asked for; the
///      controller forwards that to the server, which validates it again from scratch.
///   3. Public state (ends, boneyard, opponents' counts) is read off <see cref="Game"/>. Only the
///      legal-move list is pushed in, because that is the one thing that needs the rules.
///
/// Shipping today: DominoHandHudView, a 2D panel of buttons. The planned replacement is a 3D rack
/// of tiles held in front of the seat camera — it subclasses this, emits the same four signals,
/// and needs no change to the controller, the resolver, the rules or any RPC.
/// </summary>
[GlobalClass]
public partial class DominoHandView : Node3D
{
	protected DominoGame Game;
	protected Player Player;

	[Signal]
	public delegate void TilePlayRequestedEventHandler(int tileId, int end);

	[Signal]
	public delegate void DrawRequestedEventHandler();

	[Signal]
	public delegate void PassRequestedEventHandler();

	[Signal]
	public delegate void SurrenderRequestedEventHandler();

	public virtual void Setup(DominoGame game, Player player)
	{
		Game = game;
		Player = player;
	}

	/// <summary>
	/// Redraws everything. One entry point rather than a family of setters, so a new view cannot
	/// quietly forget to implement part of the surface.
	/// </summary>
	public virtual void Refresh(
		int[] hand,
		IReadOnlyList<MoveOption> playableMoves,
		bool isYourTurn,
		bool canDraw,
		bool mustPass)
	{
	}

	/// <summary>Greys the whole thing out — the match ended, or this player is out of it.</summary>
	public virtual void SetInteractive(bool interactive) { }

	/// <summary>Tells the view which camera is live, so it can label the view toggle correctly.</summary>
	public virtual void SetTopViewActive(bool active) { }

	/// <summary>Reports a server rejection, so the player learns why nothing happened.</summary>
	public virtual void ShowRejection(string reason) { }

	public virtual void Clear() { }
}
