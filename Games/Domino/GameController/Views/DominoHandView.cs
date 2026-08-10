using System.Collections.Generic;
using Domino.Rules;
using Godot;

/// <summary>
/// How a player sees and plays their own tiles.
///
/// The seam itself — talking only in intents, never in RPCs — lives on <see cref="SeatedHandView"/>
/// and is shared with every other seated game. What is added here is the part that IS dominoes:
/// the three things a player can ask to do with tiles, and the one call that redraws them.
///
/// Public state (ends, boneyard, opponents' counts) is read off <see cref="Game"/>. Only the legal
/// move list is pushed in, because that is the one thing that needs the rules.
/// </summary>
[GlobalClass]
public partial class DominoHandView : SeatedHandView
{
    protected DominoGame Game;

    [Signal]
    public delegate void TilePlayRequestedEventHandler(int tileId, int end);

    [Signal]
    /// <summary><paramref name="slot"/> is the place on the table the player picked, not a tile.</summary>
    public delegate void DrawRequestedEventHandler(int slot);

    [Signal]
    public delegate void PassRequestedEventHandler();

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
}
