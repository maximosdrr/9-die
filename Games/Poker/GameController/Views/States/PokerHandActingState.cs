using Godot;
using Godot.Collections;

/// <summary>
/// The action is on its way to the server.
///
/// This state exists only so the animation has somewhere to live: the request went out before the
/// transition, so the clip plays over the round trip rather than after it. Then it drops back to
/// Idle unconditionally — Idle is honest either way, and bounces straight to Looking if the turn
/// somehow came back round.
/// </summary>
[GlobalClass]
public partial class PokerHandActingState : State
{
    [Export] public float Duration = 0.35f;

    private PokerHand3DView View;
    private float _elapsed;
    private float _activeDuration;

    public PokerHandActingState() => Type = StatesRef.PokerHandActing;

    public override void Setup(Node3D parentNode) => View = parentNode as PokerHand3DView;

    public override void Enter(Dictionary metadata)
    {
        _elapsed = 0.0f;
        _activeDuration = Duration;

        var gesture = metadata != null && metadata.TryGetValue("gesture", out var requested)
            ? (PokerGesture)requested.AsInt32()
            : PokerGesture.None;

        // First person only. The seated bodies replay the same gesture off the turn context, which
        // every peer already has — including this one, so the local body is covered too.
        if (View != null)
            _activeDuration = Mathf.Max(Duration, View.PlayGesture(gesture));
    }

    public override void Process(double delta)
    {
        _elapsed += (float)delta;
        if (_elapsed < _activeDuration)
            return;

        StateMachine.ChangeState(StatesRef.PokerHandIdle, new Dictionary());
    }
}
