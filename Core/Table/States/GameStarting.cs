using Godot;
using Godot.Collections;

[GlobalClass]
public partial class GameStarting : State
{
    [Export] public Timer StartGameTimer;
    [Export] public PoolStartGameUI StartGameUI;

    public Array PlayersIds = new();

    public GameStarting()
    {
        Type = StatesRef.GameStarting;
    }

    public override void Enter(Dictionary metadata)
    {
        PlayersIds = (Array)metadata["players_ids"];

        SignalUtil.ConnectGuarded(StartGameTimer, Timer.SignalName.Timeout, new Callable(this, MethodName.OnTimeEnds));

        StartGameTimer.Start();
    }

    public override void Process(double delta)
    {
        var text = $"Game starts in {Mathf.Ceil(StartGameTimer.TimeLeft)}";
        StartGameUI.SetText(text);
    }

    private void OnTimeEnds()
    {
        StartGameUI.Hide();
        var metadata = new Dictionary { ["players_ids"] = PlayersIds };
        StateMachine.ChangeState(StatesRef.GameStarted, metadata);
        StartGameTimer.Stop();
    }
}
