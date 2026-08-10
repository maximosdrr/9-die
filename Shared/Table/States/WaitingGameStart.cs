using Godot;
using Godot.Collections;
using System.Linq;

[GlobalClass]
public partial class WaitingGameStart : State
{
    internal const int StartRequestsPerSecond = 2;

    [Export] public Area3D TableInfluence;
    [Export] public PoolStartGameUI StartGameUI;

    public Array<Node3D> PlayersInInfluenceArea = new();
    private readonly PeerRequestRateLimiter _startRequestLimiter = new(
        StartRequestsPerSecond,
        windowMilliseconds: 1_000,
        maxTrackedPeers: 16);

    public WaitingGameStart()
    {
        Type = StatesRef.GameWaitingStart;
    }

    public override void Enter(Dictionary metadata)
    {
        _startRequestLimiter.Clear();
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
        if (!@event.IsActionPressed("start_game") || PlayersInInfluenceArea.Count == 0)
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

        var requesterId = Multiplayer.GetRemoteSenderId();
        if (_startRequestLimiter.TryConsume(requesterId))
            TryStartGame(requesterId);
    }

    private void TryStartGame(int requesterId)
    {
        var currentGame = CurrentGame;
        if (StateMachine?.Current != this
            || currentGame == null
            || currentGame.IsMatchActive)
        {
            return;
        }

        // This overlap is a security decision, not presentation state. It is valid only because
        // Player.tscn never accepts client position/rotation and the bodies in this server world
        // are advanced by Player's validated server simulation.
        var bodies = TableInfluence.GetOverlappingBodies();
        var playersHere = bodies.OfType<Player>().ToList();

        if (playersHere.Count == 0)
            return;

        if (!playersHere.Any(p => (string)p.Name == requesterId.ToString()))
            return;

        if (!currentGame.CanStartWith(playersHere.Count))
            return;

        var playersIds = new Array(playersHere.Select(p => Variant.From((string)p.Name)));
        var metadata = new Dictionary { ["players_ids"] = playersIds };
        StateMachine.ChangeState(StatesRef.GameStarting, metadata);
    }

    internal bool CanAcceptStartRequest() =>
        StateMachine?.Current == this
        && CurrentGame != null
        && !CurrentGame.IsMatchActive;

    internal bool TryConsumeStartRequest(int peerId, ulong nowMilliseconds) =>
        _startRequestLimiter.TryConsume(peerId, nowMilliseconds);

    private void OnBodyEnterInInfluenceArea(Node3D body)
    {
        var bodies = TableInfluence.GetOverlappingBodies();
        PlayersInInfluenceArea = new Array<Node3D>(bodies.OfType<Player>().Cast<Node3D>());

        UpdateUiText();

        if (PlayersInInfluenceArea.Count > 0)
            StartGameUI.Show();
    }

    private void OnBodyExitedInInfluenceArea(Node3D body)
    {
        var bodies = TableInfluence.GetOverlappingBodies();
        PlayersInInfluenceArea = new Array<Node3D>(bodies.OfType<Player>().Cast<Node3D>());

        UpdateUiText();

        if (PlayersInInfluenceArea.Count == 0)
            StartGameUI.Hide();
    }

    /// <summary>
    /// The game plugged into this table, or null before Table._Ready spawned it. Read through
    /// State.Parent so the state stays reusable by any table, whatever game it hosts.
    /// </summary>
    private TableGame CurrentGame => (Parent as Table)?.CurrentTableGame;

    private void UpdateUiText()
    {
        var totalPlayers = PlayersInInfluenceArea.Count;
        var maxPlayers = CurrentGame?.MaximumPlayers ?? 4;
        var text = $"Waiting Start (Press F)\nPlayers {totalPlayers}/{maxPlayers}";
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
