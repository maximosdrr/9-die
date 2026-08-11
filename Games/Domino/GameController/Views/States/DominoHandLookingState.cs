using Godot;
using Godot.Collections;

/// <summary>
/// The player is looking at their hand: A and D step through the tiles they can actually play, and
/// the action button commits to one.
///
/// Drawing and passing are forced moves, so they need no key of their own — when there is nothing
/// to play, the same button does whatever the rules leave available.
/// </summary>
[GlobalClass]
public partial class DominoHandLookingState : State
{
    private const string InputPrevious = "move_left";
    private const string InputNext = "move_right";

    /// <summary>Left click picks the tile up; the right button puts it back down while aiming.</summary>
    private const string InputSelect = "place_action";

    [Export] public string ClipName = "HandLook";

    public DominoHand3DView View;

    public DominoHandLookingState()
    {
        Type = StatesRef.DominoHandLooking;
    }

    public override void Setup(Node3D parentNode)
    {
        View = parentNode as DominoHand3DView;
    }

    public override void Enter(Dictionary metadata)
    {
        if (View == null)
            return;

        View.SetHandVisible(true);
        View.PlayClip(ClipName);
        View.HideGhost();

        // The selection is deliberately left alone: the player was already browsing their hand
        // while waiting, and their turn arriving should not throw that away.
    }

    public override void HandleInput(InputEvent @event)
    {
        if (View == null || !View.IsYourTurn || !DominoHand3DView.InputIsLive)
            return;

        if (@event.IsActionPressed(InputPrevious))
        {
            View.SelectStep(-1);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed(InputNext))
        {
            View.SelectStep(1);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!@event.IsActionPressed(InputSelect))
            return;

        GetViewport().SetInputAsHandled();

        if (View.HasPlayableTile)
        {
            StateMachine.ChangeState(StatesRef.DominoHandAiming, new Dictionary());
            return;
        }

        // Drawing is not reachable from here any more — Process has already moved to the stock by
        // the time a stuck player could click. All that is left is the dead end: nothing to play
        // and nothing to draw.
        if (View.MustPass)
            View.RequestPassTurn();
    }

    public override void Process(double delta)
    {
        if (View == null)
            return;

        if (!View.IsYourTurn)
        {
            StateMachine.ChangeState(StatesRef.DominoHandIdle, new Dictionary());
            return;
        }

        // Nothing in hand fits, so drawing is the only move there is. Going there by itself spares
        // the player having to work out that a click they have no reason to make is what unlocks
        // the stock.
        if (!View.HasPlayableTile && View.CanDraw)
            StateMachine.ChangeState(StatesRef.DominoHandDrawing, new Dictionary());
    }
}
