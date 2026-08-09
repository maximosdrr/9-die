using System.Collections.Generic;
using Godot;
using Poker.Rules;

/// <summary>
/// How a player sees their two cards and asks to act on them.
///
/// The seam itself — talking only in intents, never in RPCs — lives on <see cref="SeatedHandView"/>
/// and is shared with every other seated game. What is added here is the part that IS poker: one
/// intent covering every betting action, and the one call that redraws.
///
/// A single <see cref="ActionRequestedEventHandler"/> rather than one signal per action, because
/// fold, check, call and raise are the same gesture at different amounts — and because the server
/// validates them through one function, so anything that splits them here could drift from it.
/// </summary>
[GlobalClass]
public partial class PokerHandView : SeatedHandView
{
	protected PokerGame Game;

	/// <summary>
	/// <paramref name="total"/> is what this player will have committed on this street after
	/// acting — never the chips added. One convention, matching <see cref="PokerBetting"/>.
	/// </summary>
	[Signal]
	public delegate void ActionRequestedEventHandler(int actionKind, int total);

	public virtual void Setup(PokerGame game, Player player)
	{
		Game = game;
		Player = player;
	}

	/// <summary>
	/// Redraws everything. One entry point rather than a family of setters, so a new view cannot
	/// quietly forget to implement part of the surface.
	/// </summary>
	public virtual void Refresh(
		int[] holeCards,
		IReadOnlyList<ActionOption> options,
		bool isYourTurn)
	{
	}
}
