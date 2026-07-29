using Godot;
using Godot.Collections;

[GlobalClass]
public partial class CueChargingState : State
{
    private const string InputSpinModifier = "spin_modifier";
    private const string InputStrokeMode = "stroke_mode";

    [ExportGroup("Sensitivity")]
    [Export] public float SpinSensitivity = 0.001f;
    [Export] public float StrokeSensitivity = 0.001f;

    [ExportGroup("Physics")]
    [Export] public float MaxDrawDistance = 0.35f;
    [Export] public float StrikeContactThreshold = 0.001f;
    [Export] public float StrikeVelocityThreshold = -0.10f;
    [Export] public float VelocitySmoothing = 18.0f;

    public Cue Cue;

    private float _currentDrawDistance;
    private float _previousDrawDistance;
    private float _smoothedVelocity;

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
        Input.MouseMode = Input.MouseModeEnum.Captured;

        _currentDrawDistance = Mathf.Max(Cue.Position.Z, Cue.BallRadiusOffset);
        _previousDrawDistance = _currentDrawDistance;
        _smoothedVelocity = 0.0f;
    }

    public override void HandleInput(InputEvent @event)
    {
        if (@event.IsActionReleased(InputStrokeMode))
        {
            StateMachine.ChangeState(StatesRef.CueIdle, new Dictionary());
            Cue.GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseMotion motion)
        {
            ProcessStrokeInput(motion.Relative);

            UpdateCueTransform();
            Cue.GetViewport().SetInputAsHandled();
        }
    }

    public override void Process(double delta)
    {
        CalculateVelocity((float)delta);

        if (CheckStrikeCondition())
        {
            var strikePower = Mathf.Abs(_smoothedVelocity);
            var success = Cue.ExecuteStrike(strikePower);

            var nextState = success ? StatesRef.CueRecover : StatesRef.CueIdle;
            StateMachine.ChangeState(nextState, new Dictionary());
        }
    }

    private void ProcessStrokeInput(Vector2 relativeMotion)
    {
        var drawDelta = relativeMotion.Y * StrokeSensitivity;
        var minDraw = Cue.BallRadiusOffset;
        var maxDraw = Cue.BallRadiusOffset + MaxDrawDistance;

        _currentDrawDistance = Mathf.Clamp(_currentDrawDistance + drawDelta, minDraw, maxDraw);
    }

    private void UpdateCueTransform()
    {
        var pos = Cue.Position;
        pos.X = Cue.SpinOffset.X;
        pos.Y = Cue.SpinOffset.Y;
        pos.Z = _currentDrawDistance;
        Cue.Position = pos;
    }

    private void CalculateVelocity(float delta)
    {
        if (delta <= 0.0f)
            return;

        var instantVelocity = (_currentDrawDistance - _previousDrawDistance) / delta;
        _previousDrawDistance = _currentDrawDistance;

        var weight = Mathf.Clamp(delta * VelocitySmoothing, 0.0f, 1.0f);
        _smoothedVelocity = Mathf.Lerp(_smoothedVelocity, instantVelocity, weight);
    }

    private bool CheckStrikeCondition()
    {
        var isTouchingBall = _currentDrawDistance <= (Cue.BallRadiusOffset + StrikeContactThreshold);
        var isMovingForward = _smoothedVelocity < StrikeVelocityThreshold;

        return isTouchingBall && isMovingForward;
    }
}
