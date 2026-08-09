using Godot;

/// <summary>
/// The aiming dot at the centre of the screen.
///
/// Computes its own centre from its size rather than relying on anchors, the same way CuePowerBar
/// places itself — it then sits correctly at any resolution with no layout to get wrong. A dark
/// ring behind the dot keeps it readable against a pale tabletop.
/// </summary>
[GlobalClass]
public partial class DominoHandCrosshair : Control
{
	[Export] public float Radius = 3.5f;
	[Export] public Color DotColor = new(1.0f, 1.0f, 1.0f);
	[Export] public Color RingColor = new(0.0f, 0.0f, 0.0f, 0.55f);

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;
		Resized += QueueRedraw;
		Hide();
	}

	public override void _Draw()
	{
		var centre = Size * 0.5f;
		DrawArc(centre, Radius + 1.2f, 0.0f, Mathf.Tau, 20, RingColor, 1.6f, true);
		DrawCircle(centre, Radius, DotColor);
	}
}
