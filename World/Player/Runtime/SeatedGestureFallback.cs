using Godot;

/// <summary>
/// Small rig-independent fallback for seated gestures whose authored character clip is not
/// available yet. It moves only the replaceable visual root; gameplay position and collision never
/// change. A future character replaces it simply by providing the named AnimationPlayer clip.
/// </summary>
[GlobalClass]
public partial class SeatedGestureFallback : Node
{
    [Export] public Node3D VisualRoot;
    [Export] public float PushReach = 0.025f;
    [Export] public float PushDistance = 0.045f;
    [Export] public float PushDip = 0.010f;
    [Export] public float PushDuration = 0.82f;

    private Tween _activeTween;
    private Transform3D _rest;
    private bool _hasRest;

    public float Play(string animationName, Vector3 tableTarget)
    {
        if (animationName != PokerClips.BodyThrowChips || !IsInstanceValid(VisualRoot))
            return 0.0f;

        StopAndRestore();
        _rest = VisualRoot.Transform;
        _hasRest = true;

        var parent = VisualRoot.GetParentOrNull<Node3D>();
        var localTarget = parent != null ? parent.ToLocal(tableTarget) : tableTarget;
        var toward = localTarget - _rest.Origin;
        toward.Y = 0.0f;
        toward = toward.LengthSquared() < 1e-6f ? -Vector3.Forward : toward.Normalized();

        var reach = new Transform3D(_rest.Basis,
            _rest.Origin + toward * PushReach - Vector3.Up * (PushDip * 0.35f));
        var press = new Transform3D(_rest.Basis,
            _rest.Origin + toward * PushDistance - Vector3.Up * PushDip);

        // The three beats read as reach -> press -> release. Tweening the visual root is deliberately
        // conservative: it remains valid for a replacement mesh whose arm bone names are different.
        _activeTween = CreateTween();
        _activeTween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _activeTween.TweenProperty(VisualRoot, new NodePath("transform"), reach,
            PushDuration * 0.22f);
        _activeTween.TweenProperty(VisualRoot, new NodePath("transform"), press,
            PushDuration * 0.30f);
        _activeTween.TweenProperty(VisualRoot, new NodePath("transform"), _rest,
            PushDuration * 0.48f);
        _activeTween.Finished += OnTweenFinished;
        return PushDuration;
    }

    public override void _ExitTree() => StopAndRestore();

    private void OnTweenFinished()
    {
        if (_hasRest && IsInstanceValid(VisualRoot))
            VisualRoot.Transform = _rest;
        _hasRest = false;
        _activeTween = null;
    }

    private void StopAndRestore()
    {
        if (_activeTween != null && _activeTween.IsValid())
            _activeTween.Kill();
        _activeTween = null;
        if (_hasRest && IsInstanceValid(VisualRoot))
            VisualRoot.Transform = _rest;
        _hasRest = false;
    }
}
