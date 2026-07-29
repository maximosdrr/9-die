using Godot;
using Godot.Collections;

[GlobalClass]
public partial class CueRecoverState : State
{
    public Cue Cue;
    private Tween _tween;

    public CueRecoverState()
    {
        Type = StatesRef.CueRecover;
    }

    public override void Setup(Node3D parentNode)
    {
        Cue = parentNode as Cue;
    }

    public override void Enter(Dictionary metadata)
    {
        _tween?.Kill();

        _tween = Cue.CreateTween();
        _tween.SetParallel(true);
        _tween.TweenProperty(Cue, "position:z", Cue.BallRadiusOffset, 0.2)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        _tween.TweenProperty(Cue, "SpinOffset", Vector2.Zero, 0.5)
            .SetEase(Tween.EaseType.Out);
        ResetElevation();
        _tween.SetParallel(false);

        var timeToWait = Mathf.Max(Cue.PostShotCooldown, 0.5f);

        _tween.TweenInterval(timeToWait - 0.5);
        _tween.TweenCallback(Callable.From(OnCooldownFinished));
    }

    public override void Exit(Dictionary metadata)
    {
        if (_tween != null)
        {
            _tween.Kill();
            _tween = null;
        }
    }

    public override void HandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion)
        {
            if (Cue.GetViewport() != null)
                Cue.GetViewport().SetInputAsHandled();
        }
    }

    private void OnCooldownFinished()
    {
        StateMachine.ChangeState(StatesRef.CueLocked, new Dictionary());
    }

    private void ResetElevation()
    {
        Cue.CurrentElevation = 0.0f;
        var rotDeg = Cue.RotationDegrees;
        rotDeg.X = 0.0f;
        Cue.RotationDegrees = rotDeg;
    }
}
