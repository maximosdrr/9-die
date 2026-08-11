using Godot;
using Godot.Collections;
using Poker.Rules;

/// <summary>
/// Somebody else is acting.
///
/// The cards stay in hand and stay peekable on purpose: a player watching a hand play out should be
/// able to look at what they are holding and plan, exactly as they would at a table. Only the
/// action keys are dead, and they say why rather than doing nothing.
/// </summary>
[GlobalClass]
public partial class PokerHandIdleState : State
{
    [Export] public string ClipName = PokerClips.Idle;

    private PokerHand3DView View;

    public PokerHandIdleState() => Type = StatesRef.PokerHandIdle;

    public override void Setup(Node3D parentNode) => View = parentNode as PokerHand3DView;

    public override void Enter(Dictionary metadata)
    {
        // StateMachine.SetupInitialState calls this during _Ready, before the view has its game.
        View?.PlayClip(ClipName);
    }

    public override void Process(double delta)
    {
        if (View is { IsYourTurn: true, HasAnyAction: true })
            StateMachine.ChangeState(StatesRef.PokerHandLooking, new Dictionary());
    }
}
