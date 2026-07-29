using Godot;
using Godot.Collections;
using System.Linq;

[GlobalClass]
public partial class WaitingGameStart : State
{
    [Export] public Area3D TableInfluence;
    [Export] public PoolStartGameUI StartGameUI;

    public Array<Node3D> PlayersOnInfluencyArea = new();

    public WaitingGameStart()
    {
        Type = StatesRef.GameWaitingStart;
    }

    public override void Enter(Dictionary metadata)
    {
        var isRestart = metadata.ContainsKey("is_restart");
        if (isRestart)
        {
            UpdateUiText();
            StartGameUI.Show();
        }
        else
        {
            StartGameUI.Hide();
        }

        ConnectSignals();
    }

    public override void Exit(Dictionary metadata)
    {
        DisconnectSignals();
    }

    public override void Process(double delta)
    {
        if (Input.IsActionJustPressed("start_game"))
        {
            if (PlayersOnInfluencyArea.Count >= 1)
            {
                var playersIds = new Array(PlayersOnInfluencyArea.Select(p => Variant.From((string)p.Name)));
                var metadata = new Dictionary { ["players_ids"] = playersIds };
                StateMachine.ChangeState(StatesRef.GameStarting, metadata);
            }
        }
    }

    private void OnBodyEnterInInfluenceArea(Node3D body)
    {
        var bodies = TableInfluence.GetOverlappingBodies();
        PlayersOnInfluencyArea = new Array<Node3D>(bodies.OfType<Player>().Cast<Node3D>());

        UpdateUiText();

        if (PlayersOnInfluencyArea.Count > 0)
            StartGameUI.Show();
    }

    private void OnBodyExitedInInfluenceArea(Node3D body)
    {
        var bodies = TableInfluence.GetOverlappingBodies();
        PlayersOnInfluencyArea = new Array<Node3D>(bodies.OfType<Player>().Cast<Node3D>());

        UpdateUiText();

        if (PlayersOnInfluencyArea.Count == 0)
            StartGameUI.Hide();
    }

    private void UpdateUiText()
    {
        var totalPlayers = PlayersOnInfluencyArea.Count;
        var text = $"Waiting Start (Press F)\nPlayers {totalPlayers}/4";
        StartGameUI.SetText(text);
    }

    private void ConnectSignals()
    {
        SignalUtil.ConnectGuarded(TableInfluence, Area3D.SignalName.BodyEntered, new Callable(this, MethodName.OnBodyEnterInInfluenceArea));
        SignalUtil.ConnectGuarded(TableInfluence, Area3D.SignalName.BodyExited, new Callable(this, MethodName.OnBodyExitedInInfluenceArea));
    }

    private void DisconnectSignals()
    {
        SignalUtil.DisconnectGuarded(TableInfluence, Area3D.SignalName.BodyEntered, new Callable(this, MethodName.OnBodyEnterInInfluenceArea));
        SignalUtil.DisconnectGuarded(TableInfluence, Area3D.SignalName.BodyExited, new Callable(this, MethodName.OnBodyExitedInInfluenceArea));
    }
}
