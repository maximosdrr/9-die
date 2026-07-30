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

    public override void HandleInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("start_game") || PlayersOnInfluencyArea.Count == 0)
            return;

        RequestStartGame();
        GetViewport().SetInputAsHandled();
    }

    private void RequestStartGame()
    {
        if (Multiplayer.IsServer())
            TryStartGame(Multiplayer.GetUniqueId());
        else
            RpcId(1, MethodName.RequestStartGameOnServer);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestStartGameOnServer()
    {
        if (!Multiplayer.IsServer())
            return;

        TryStartGame(Multiplayer.GetRemoteSenderId());
    }

    private void TryStartGame(int requesterId)
    {
        var bodies = TableInfluence.GetOverlappingBodies();
        var playersHere = bodies.OfType<Player>().ToList();

        if (playersHere.Count == 0)
            return;

        if (!playersHere.Any(p => (string)p.Name == requesterId.ToString()))
            return;

        var playersIds = new Array(playersHere.Select(p => Variant.From((string)p.Name)));
        var metadata = new Dictionary { ["players_ids"] = playersIds };
        StateMachine.ChangeState(StatesRef.GameStarting, metadata);
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
