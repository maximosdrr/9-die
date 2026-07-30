using Godot;
using Godot.Collections;

[GlobalClass]
public partial class CueJumpState : State
{
	private const string InputElevationModifier = "elevation_modifier";

	public Cue Cue;

	public CueJumpState()
	{
		Type = StatesRef.CueJumping;
	}

	public override void Setup(Node3D parentNode)
	{
		Cue = parentNode as Cue;
	}

	public override void Enter(Dictionary metadata)
	{
		var safeAngleDeg = Mathf.RadToDeg(Cue.MinSafeAngle);
		SetVisualElevation(safeAngleDeg);
	}

	public override void HandleInput(InputEvent @event)
	{
		if (@event.IsActionReleased(InputElevationModifier))
		{
			StateMachine.ChangeState(StatesRef.CueIdle, new Dictionary());
			Cue.GetViewport().SetInputAsHandled();
			return;
		}

		if (@event is InputEventMouseButton mb && mb.Pressed)
		{
			if (mb.ButtonIndex == MouseButton.WheelUp)
			{
				AdjustElevation(1);
				Cue.GetViewport().SetInputAsHandled();
				return;
			}

			if (mb.ButtonIndex == MouseButton.WheelDown)
			{
				AdjustElevation(-1);
				Cue.GetViewport().SetInputAsHandled();
				return;
			}
		}
	}

	public override void Process(double delta)
	{
		Cue.SnapToRestPose();
	}

	private void AdjustElevation(int direction)
	{
		var step = direction * Cue.ElevationSensitivity;
		var newAngle = Cue.CurrentElevation - step;

		var safeLimitDeg = Mathf.RadToDeg(Cue.MinSafeAngle);
		var clampedAngle = Mathf.Clamp(newAngle, Cue.JumpMaxAngle, safeLimitDeg);

		SetVisualElevation(clampedAngle);
	}

	private void SetVisualElevation(float angle)
	{
		Cue.CurrentElevation = angle;
		var rotDeg = Cue.RotationDegrees;
		rotDeg.X = Cue.CurrentElevation;
		Cue.RotationDegrees = rotDeg;
	}
}
