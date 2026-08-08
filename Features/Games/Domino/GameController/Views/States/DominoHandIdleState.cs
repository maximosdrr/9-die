using Godot;
using Godot.Collections;

/// <summary>
/// Somebody else's turn. The player still holds their tiles and can look through them — planning
/// the next move while an opponent thinks is most of what a domino player does — but nothing they
/// press puts anything down.
/// </summary>
[GlobalClass]
public partial class DominoHandIdleState : State
{
	private const string InputPrevious = "move_left";
	private const string InputNext = "move_right";
	private const string InputSelect = "place_action";

	[Export] public string ClipName = "HandIdle";

	public DominoHand3DView View;

	public DominoHandIdleState()
	{
		Type = StatesRef.DominoHandIdle;
	}

	public override void Setup(Node3D parentNode)
	{
		View = parentNode as DominoHand3DView;
	}

	public override void Enter(Dictionary metadata)
	{
		// StateMachine._Ready calls this directly, before the view has been handed its game, so
		// nothing here may assume the view is configured — or even present.
		if (View == null)
			return;

		View.SetHandVisible(true);
		View.HideGhost();
		View.SetCrosshairVisible(false);
		View.PlayClip(ClipName);
	}

	public override void HandleInput(InputEvent @event)
	{
		if (View == null || !DominoHand3DView.InputIsLive)
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

		// Looking through the hand is free; committing to a tile is not.
		if (!@event.IsActionPressed(InputSelect))
			return;

		View.ShowNotice("Não é a sua vez", 1.5f);
		GetViewport().SetInputAsHandled();
	}

	public override void Process(double delta)
	{
		if (View is { IsYourTurn: true })
			StateMachine.ChangeState(StatesRef.DominoHandLooking, new Dictionary());
	}
}
