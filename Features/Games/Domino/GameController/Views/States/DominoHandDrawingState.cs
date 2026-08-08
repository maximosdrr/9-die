using Godot;
using Godot.Collections;

/// <summary>
/// Nothing in hand fits, so the player is picking a face-down tile off the table. They choose a
/// PLACE, not a tile — what was lying there is only revealed once it reaches their hand.
/// </summary>
[GlobalClass]
public partial class DominoHandDrawingState : State
{
	/// <summary>Left click, like laying a tile: both act on whatever the crosshair is over.</summary>
	private const string InputTake = "place_action";

	private const string InputCancel = "cancel_action";

	[Export] public string ClipName = "HandPrepare";

	public DominoHand3DView View;

	public DominoHandDrawingState()
	{
		Type = StatesRef.DominoHandDrawing;
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
		View.SetCrosshairVisible(true);
		View.ShowNotice("Mire uma peça do monte e clique", 3.0f);
	}

	public override void Exit(Dictionary metadata)
	{
		View?.SetCrosshairVisible(false);
	}

	public override void HandleInput(InputEvent @event)
	{
		if (View == null || !View.IsYourTurn || !DominoHand3DView.InputIsLive)
			return;

		if (@event.IsActionPressed(InputCancel))
		{
			StateMachine.ChangeState(StatesRef.DominoHandLooking, new Dictionary());
			GetViewport().SetInputAsHandled();
			return;
		}

		if (!@event.IsActionPressed(InputTake))
			return;

		View.RequestDrawAimed();
		GetViewport().SetInputAsHandled();
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

		// The drawn tile has landed and something is playable now, so the player goes back to
		// choosing. Driven by the hand contents rather than by the reply, because the hand only
		// changes when the server has actually granted the draw.
		if (View.HasPlayableTile)
		{
			StateMachine.ChangeState(StatesRef.DominoHandLooking, new Dictionary());
			return;
		}

		View.UpdateStockAimFromCrosshair();
	}
}
