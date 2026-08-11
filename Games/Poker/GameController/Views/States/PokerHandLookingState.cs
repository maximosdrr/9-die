using Godot;
using Godot.Collections;
using Poker.Rules;

/// <summary>
/// Your turn. The crosshair turns the table into the interface: chips are selected and returned
/// with one click, and check, fold or wager confirmation use one click on their hovered chalk zone.
/// All-in remains the only keyboard shortcut.
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

        if (@event is InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true,
            })
        {
            View.GetViewport().SetInputAsHandled();
            var gesture = View.HandleTableClick();
            if (gesture != PokerGesture.None)
                BeginGesture(gesture);
            return;
        }

        if (@event.IsActionPressed(PokerInput.AllIn))
            ActAllIn();
    }

    private bool Blocked()
    {
        if (View.HasPickedUpCards)
            return false;

        View.ShowNotice("Aguarde — você ainda está olhando suas cartas", 1.5f);
        return true;
    }

    private void ActAllIn()
    {
        View.GetViewport().SetInputAsHandled();

        if (Blocked())
            return;

        if (!View.TryAllIn(out var kind, out var total))
        {
            View.ShowNotice("Você não tem como ir de all-in agora", 1.5f);
            return;
        }

        // The shortcut supersedes any tentative manual amount. The established authoritative
        // animation then removes the whole stack from one coherent source.
        View.CancelPreparedWager(immediate: true);
        View.RequestAction(kind, total);
        BeginGesture(PokerGesture.ThrowChips);
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
