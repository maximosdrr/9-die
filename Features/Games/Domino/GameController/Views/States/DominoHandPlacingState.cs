using Godot;
using Godot.Collections;

/// <summary>
/// The tile is going down. Input is locked while the hand travels to the table.
///
/// The request goes out as the movement STARTS, so the network round trip happens under the
/// animation instead of after it. If the server refuses, the turn simply never changes and the
/// player lands back in Idle with the tile still in hand — the board is only ever redrawn from
/// what the server confirmed.
/// </summary>
[GlobalClass]
public partial class DominoHandPlacingState : State
{
	[Export] public string ClipName = "HandPlace";

	/// <summary>How long the placeholder takes to "reach" the table. The real clip replaces this.</summary>
	[Export] public float Duration = 0.35f;

	public DominoHand3DView View;

	private float _elapsed;

	public DominoHandPlacingState()
	{
		Type = StatesRef.DominoHandPlacing;
	}

	public override void Setup(Node3D parentNode)
	{
		View = parentNode as DominoHand3DView;
	}

	public override void Enter(Dictionary metadata)
	{
		_elapsed = 0.0f;

		if (View == null)
			return;

		View.PlayClip(ClipName);
		View.RequestPlaySelected();
		View.HideGhost();
	}

	public override void Process(double delta)
	{
		_elapsed += (float)delta;
		if (_elapsed < Duration)
			return;

		// Idle is the honest destination either way: it bounces straight back to Looking if the
		// turn is somehow still this player's, and stays put if the tile went down.
		StateMachine.ChangeState(StatesRef.DominoHandIdle, new Dictionary());
	}
}
