using Godot;
using Godot.Collections;

/// <summary>
/// The tile is chosen and the player is deciding where it goes. A and D turn it over; the ghost on
/// the table says whether the end being aimed at will take the half they are presenting.
///
/// Which end is aimed at comes from the view. Today it takes the first legal one; the crosshair
/// will feed it whatever the player is pointing at without this state changing.
/// </summary>
[GlobalClass]
public partial class DominoHandAimingState : State
{
	private const string InputRotateLeft = "move_left";
	private const string InputRotateRight = "move_right";
	private const string InputAction = "interact";
	private const string InputCancel = "cancel_action";

	[Export] public string ClipName = "HandPrepare";

	public DominoHand3DView View;

	public DominoHandAimingState()
	{
		Type = StatesRef.DominoHandAiming;
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
		View.AimAtDefaultEnd();
		View.SetCrosshairVisible(true);
		View.UpdateGhost();
	}

	public override void Exit(Dictionary metadata)
	{
		View?.HideGhost();
		View?.SetCrosshairVisible(false);
	}

	public override void HandleInput(InputEvent @event)
	{
		if (View == null || !View.IsYourTurn)
			return;

		if (@event.IsActionPressed(InputCancel))
		{
			StateMachine.ChangeState(StatesRef.DominoHandLooking, new Dictionary());
			GetViewport().SetInputAsHandled();
			return;
		}

		// Either direction turns the tile: with two halves there is only one thing to turn to, and
		// making the player learn which key means which would be noise.
		if (@event.IsActionPressed(InputRotateLeft) || @event.IsActionPressed(InputRotateRight))
		{
			View.RotateSelected();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (!@event.IsActionPressed(InputAction))
			return;

		GetViewport().SetInputAsHandled();

		// A tile held the wrong way round simply does not go down — the ghost has been red the
		// whole time saying so.
		if (View.CanPlaceNow())
			StateMachine.ChangeState(StatesRef.DominoHandPlacing, new Dictionary());
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

		// The end follows the crosshair every frame, so the ghost tracks the player's head rather
		// than waiting for a click.
		View.UpdateAimFromCrosshair();
	}
}
