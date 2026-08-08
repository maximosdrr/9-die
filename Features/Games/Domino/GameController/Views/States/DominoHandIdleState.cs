using Godot;
using Godot.Collections;

/// <summary>
/// Not this player's turn: the hand is down and nothing can be selected.
/// </summary>
[GlobalClass]
public partial class DominoHandIdleState : State
{
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

		View.SetHandVisible(false);
		View.HideGhost();
		View.PlayClip(ClipName);
	}

	public override void Process(double delta)
	{
		if (View is { IsYourTurn: true })
			StateMachine.ChangeState(StatesRef.DominoHandLooking, new Dictionary());
	}
}
