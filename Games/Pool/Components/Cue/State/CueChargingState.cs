using Godot;
using Godot.Collections;

/// <summary>
/// The real stroke gesture: pull the cue back, then push it forward, and the shot fires the
/// moment the tip reaches the ball. No button to release — the stroke itself is the trigger.
///
/// Backswing depth selects the intended power; forward speed contributes a bounded timing
/// efficiency. Normalising velocity by the configured full-draw distance keeps the timing term
/// independent of frame rate while sensitivity remains available for different mice.
/// </summary>
[GlobalClass]
public partial class CueChargingState : State
{
    private const string InputStrokeMode = "stroke_mode";

    [ExportGroup("Draw")]
    /// <summary>Screen pixels of backswing for a full-power shot at sensitivity 1.</summary>
    [Export] public float FullDrawPixels = 50.0f;

    /// <summary>Player-facing multiplier, so different mice can be matched to the same hand movement.</summary>
    [Export] public float Sensitivity = 1.0f;

    /// <summary>How far back the cue visibly travels at full draw, in metres.</summary>
    [Export] public float MaxDrawDistance = 0.35f;

    /// <summary>How close to the ball the tip must return for the stroke to land.</summary>
    [Export] public float ContactThreshold = 0.01f;

    /// <summary>Below this backswing the forward stroke is a practice stroke, not a shot.</summary>
    [Export] public float MinPowerToFire = 0.03f;

    [ExportGroup("Stroke Timing")]
    /// <summary>Forward travel in full-draws per second that delivers 100% of the loaded power.</summary>
    [Export] public float ReferenceForwardRate = 2.5f;

    /// <summary>
    /// Slow strokes retain enough authority for accessibility, but no longer hit exactly like a
    /// fast acceleration through the ball.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float MinimumTimingEfficiency = 0.72f;

    public Cue Cue;

    private float _drawFraction;
    private float _peakDraw;
    private float _peakForwardRate;
    private bool _strokingForward;

    public CueChargingState()
    {
        Type = StatesRef.CueCharging;
    }

    public override void Setup(Node3D parentNode)
    {
        Cue = parentNode as Cue;
    }

    public override void Enter(Dictionary metadata)
    {
        InputFocus.Capture();

        _drawFraction = 0.0f;
        _peakDraw = 0.0f;
        _peakForwardRate = 0.0f;
        _strokingForward = false;

        Cue.SetCharge(true, 0.0f);
        UpdateCueTransform();
    }

    public override void Exit(Dictionary metadata)
    {
        Cue.SetCharge(false, 0.0f);
    }

    public override void HandleInput(InputEvent @event)
    {
        // Letting go of the modifier abandons the stroke — the escape hatch, not the trigger.
        if (@event.IsActionReleased(InputStrokeMode))
        {
            StateMachine.ChangeState(StatesRef.CueIdle, new Dictionary());
            Cue.GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventMouseMotion motion)
            return;

        var screenDelta = PointerMotion.ReadScreenDelta(motion);
        var screenVelocity = PointerMotion.ReadScreenVelocity(motion);
        ApplyStroke(screenDelta.Y, screenVelocity.Y);
        UpdateCueTransform();
        Cue.GetViewport().SetInputAsHandled();

        if (ShouldFire())
            Fire();
    }

    /// <summary>
    /// ScreenRelative is the unscaled motion, so the draw does not change with window size or
    /// stretch mode the way Relative does. It reads zero on some captured-mouse configurations,
    /// hence the fallback — the two are identical whenever content scale is 1:1.
    /// </summary>
    private void ApplyStroke(float verticalPixels, float verticalPixelsPerSecond)
    {
        if (FullDrawPixels <= 0.0f)
            return;

        // Pulling the mouse toward you draws the cue back; pushing away strokes through the ball.
        var next = Mathf.Clamp(
            _drawFraction + verticalPixels * Sensitivity / FullDrawPixels,
            0.0f,
            1.0f);

        if (next > _drawFraction)
        {
            // Turning back after a forward move starts a fresh backswing, so practice strokes
            // don't leave stale power loaded from an earlier one.
            if (_strokingForward)
            {
                _peakDraw = 0.0f;
                _peakForwardRate = 0.0f;
            }

            _strokingForward = false;
            _peakDraw = Mathf.Max(_peakDraw, next);
            Cue.SetCharge(true, _peakDraw);
        }
        else if (next < _drawFraction)
        {
            _strokingForward = true;
            var forwardRate = -verticalPixelsPerSecond * Sensitivity / FullDrawPixels;
            _peakForwardRate = Mathf.Max(_peakForwardRate, forwardRate);
            Cue.SetCharge(true, DeliveredPower());
        }

        _drawFraction = next;
    }

    private bool ShouldFire()
    {
        return _strokingForward
               && _drawFraction <= ContactThreshold
               && _peakDraw >= MinPowerToFire;
    }

    private void Fire()
    {
        var fired = Cue.ExecuteStrikeWithPower(DeliveredPower());
        StateMachine.ChangeState(fired ? StatesRef.CueRecover : StatesRef.CueIdle, new Dictionary());
    }

    private float DeliveredPower()
    {
        return ComputeDeliveredPower(
            _peakDraw, _peakForwardRate, ReferenceForwardRate, MinimumTimingEfficiency);
    }

    public static float ComputeDeliveredPower(
        float loadedPower, float forwardRate, float referenceRate, float minimumEfficiency)
    {
        var safeLoadedPower = Mathf.Clamp(loadedPower, 0.0f, 1.0f);
        var safeMinimum = Mathf.Clamp(minimumEfficiency, 0.0f, 1.0f);
        var timing = referenceRate > 0.0f
            ? Mathf.Clamp(forwardRate / referenceRate, 0.0f, 1.0f)
            : 1.0f;
        return safeLoadedPower * Mathf.Lerp(safeMinimum, 1.0f, timing);
    }

    private void UpdateCueTransform()
    {
        var pos = Cue.Position;
        pos.X = Cue.SpinOffset.X;
        pos.Y = Cue.SpinOffset.Y;
        pos.Z = Cue.BallRadiusOffset + _drawFraction * MaxDrawDistance;
        Cue.Position = pos;
    }
}
