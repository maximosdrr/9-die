using Godot;
using Godot.Collections;
using Poker.Rules;

/// <summary>
/// Your turn. The crosshair turns the table into the interface: chips are selected and returned
/// with one click, and check, fold or wager confirmation use one click on their hovered chalk zone.
/// A short CALL/AUTO press pays or makes the minimum bet; holding it for one second goes all-in.
/// </summary>
[GlobalClass]
public partial class PokerHandLookingState : State
{
    [Export] public string ClipName = PokerClips.Idle;

    private PokerHand3DView View;

    public PokerHandLookingState() => Type = StatesRef.PokerHandLooking;

    public override void Setup(Node3D parentNode) => View = parentNode as PokerHand3DView;

    public override void Enter(Dictionary metadata) => View?.PlayClip(ClipName);

    public override void HandleInput(InputEvent @event)
    {
        if (View == null || !PokerHand3DView.InputIsLive || !View.IsYourTurn)
            return;

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
        {
            View.GetViewport().SetInputAsHandled();
            var gesture = mouse.Pressed
                ? View.HandleTableClick()
                : View.HandleTableRelease();
            if (gesture != PokerGesture.None)
                BeginGesture(gesture);
        }
    }

    private void BeginGesture(PokerGesture gesture) =>
        StateMachine.ChangeState(StatesRef.PokerHandActing,
            new Dictionary { ["gesture"] = (int)gesture });

    public override void Process(double delta)
    {
        if (View != null && View.TryConsumeTableGesture(out var gesture))
        {
            BeginGesture(gesture);
            return;
        }

        if (View != null && (!View.IsYourTurn || !View.HasAnyAction))
            StateMachine.ChangeState(StatesRef.PokerHandIdle, new Dictionary());
    }
}
