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

        var followDuration = Mathf.Max(0.01f, Cue.FollowThroughDuration);
        var retractDuration = Mathf.Max(0.01f, 0.20f - followDuration);

        _tween = Cue.CreateTween();
        _tween.TweenProperty(
                Cue, "position:z", Cue.BallRadiusOffset - Cue.FollowThroughDistance, followDuration)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        _tween.TweenProperty(Cue, "position:z", Cue.BallRadiusOffset, retractDuration)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        _tween.Parallel().TweenProperty(Cue, "SpinOffset", Vector2.Zero, retractDuration)
            .SetEase(Tween.EaseType.Out);
        ResetElevation();

        var timeToWait = Mathf.Max(Cue.PostShotCooldown, 0.5f);
        _tween.TweenInterval(Mathf.Max(0.0f, timeToWait - 0.2f));
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
        Cue.CompletePostShotRecovery();
    }

    private void ResetElevation()
    {
        Cue.CurrentElevation = 0.0f;
        var rotDeg = Cue.RotationDegrees;
        rotDeg.X = 0.0f;
        Cue.RotationDegrees = rotDeg;
    }
}
