using Godot;
using Godot.Collections;
using Poker.Rules;

/// <summary>
/// Your turn.
///
/// Every key acts immediately — there is no highlighted option and no confirmation. What makes that
/// safe for a raise is that its SIZE was decided before the key was pressed: A/D move a number the
/// HUD is always showing, so R can only ever send what the player was already looking at.
///
/// A key for an action that is not on offer does nothing but say so. The list comes from the same
/// pure function the server re-runs, so "not on offer" here and "refused" there are the same set.
/// </summary>
[GlobalClass]
public partial class PokerHandLookingState : State
{
    [Export] public string ClipName = PokerClips.Idle;

    private PokerHand3DView View;

    public PokerHandLookingState() => Type = StatesRef.PokerHandLooking;

    public override void Setup(Node3D parentNode) => View = parentNode as PokerHand3DView;

    public override void Enter(Dictionary metadata)
    {
        View?.PlayClip(ClipName);
    }

    public override void HandleInput(InputEvent @event)
    {
        if (View == null || !PokerHand3DView.InputIsLive || !View.IsYourTurn)
            return;

        if (@event.IsActionPressed(PokerInput.StepDown) || @event.IsActionPressed(PokerInput.StepUp))
        {
            View.StepRaise(@event.IsActionPressed(PokerInput.StepUp) ? 1 : -1);
            View.GetViewport().SetInputAsHandled();
            return;
        }

        if (@event.IsActionPressed(PokerInput.Fold))
        {
            Act(PokerActionKind.Fold, PokerGesture.Fold);
            return;
        }

        if (@event.IsActionPressed(PokerInput.Raise))
        {
            Act(PokerActionKind.Raise, PokerGesture.ThrowChips);
            return;
        }

        if (@event.IsActionPressed(PokerInput.Call))
        {
            // One key for both, because from the player's side they are the same decision — stay in
            // the hand for whatever it currently costs, which is sometimes nothing.
            if (View.HasAction(PokerActionKind.Check))
                Act(PokerActionKind.Check, PokerGesture.Knock);
            else
                Act(PokerActionKind.Call, PokerGesture.ThrowChips);

            return;
        }

        if (@event.IsActionPressed(PokerInput.AllIn))
            ActAllIn();
    }

    /// <summary>
    /// Nothing may be decided before the cards have been looked at. The only thing still allowed is
    /// moving the head, which the controller owns and never routes through here.
    /// </summary>
    private bool Blocked()
    {
        if (View.HasPickedUpCards)
            return false;

        View.ShowNotice("Aguarde — você ainda está olhando suas cartas", 1.5f);
        return true;
    }

    private void Act(PokerActionKind kind, PokerGesture gesture)
    {
        View.GetViewport().SetInputAsHandled();

        if (Blocked())
            return;

        if (!View.HasAction(kind))
        {
            View.ShowNotice("Essa ação não está disponível agora", 1.5f);
            return;
        }

        Send(kind, View.TotalFor(kind), gesture);
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

        Send(kind, total, PokerGesture.ThrowChips);
    }

    private void Send(PokerActionKind kind, int total, PokerGesture gesture)
    {
        View.RequestAction(kind, total);
        StateMachine.ChangeState(StatesRef.PokerHandActing, new Dictionary { ["gesture"] = (int)gesture });
    }

    public override void Process(double delta)
    {
        if (View != null && (!View.IsYourTurn || !View.HasAnyAction))
            StateMachine.ChangeState(StatesRef.PokerHandIdle, new Dictionary());
    }
}
