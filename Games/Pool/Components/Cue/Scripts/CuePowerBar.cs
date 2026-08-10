using Godot;

/// <summary>
/// Shows the power loaded into the current stroke — the depth of the backswing, which holds
/// steady while the cue travels forward so you can see what is about to be delivered.
///
/// This is not decoration: it is the feedback loop that makes the control learnable. Reviews of
/// games that ship stroke-based power without a meter consistently land on the same complaint,
/// that you can only guess at strength until you have played for a while.
/// </summary>
[GlobalClass]
public partial class CuePowerBar : Control
{
    [Export] public Cue Cue;

    [ExportGroup("Layout")]
    [Export] public Vector2 BarSize = new(20.0f, 200.0f);
    [Export] public float MarginRight = 64.0f;

    [ExportGroup("Colours")]
    [Export] public Color BackgroundColor = new(0.0f, 0.0f, 0.0f, 0.45f);
    [Export] public Color BorderColor = new(1.0f, 1.0f, 1.0f, 0.55f);
    [Export] public Color SoftColor = new(0.35f, 0.85f, 0.4f);
    [Export] public Color HardColor = new(0.95f, 0.3f, 0.2f);

    private float _power;
    private bool _charging;

    public override void _Ready()
    {
        // Covering the whole viewport and drawing the bar at a computed spot avoids any anchor
        // maths, and keeps the bar correctly placed at any resolution.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        if (Cue == null && GetParent()?.GetParent() is Node parent)
            Cue = parent.GetNodeOrNull<Cue>("AimPivot/Cue");

        if (Cue != null)
            Cue.ChargeChanged += OnChargeChanged;

        Visible = false;
    }

    public override void _ExitTree()
    {
        if (Cue != null)
            Cue.ChargeChanged -= OnChargeChanged;
    }

    private void OnChargeChanged(bool charging, float power)
    {
        // Only the player actually aiming should see their own meter.
        if (Cue != null && !Cue.IsMultiplayerAuthority())
            return;

        _charging = charging;
        _power = power;

        Visible = charging;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_charging)
            return;

        var origin = new Vector2(
            Size.X - MarginRight - BarSize.X,
            (Size.Y - BarSize.Y) * 0.5f);

        var outer = new Rect2(origin, BarSize);
        DrawRect(outer, BackgroundColor);

        var fillHeight = BarSize.Y * Mathf.Clamp(_power, 0.0f, 1.0f);
        var fill = new Rect2(
            new Vector2(origin.X, origin.Y + BarSize.Y - fillHeight),
            new Vector2(BarSize.X, fillHeight));

        DrawRect(fill, SoftColor.Lerp(HardColor, _power));
        DrawRect(outer, BorderColor, filled: false, width: 2.0f);
    }
}
