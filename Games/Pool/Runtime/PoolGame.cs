using Godot;
using Godot.Collections;

[GlobalClass]
public partial class PoolGame : TableGame
{
    public override int MinimumPlayers => 1;
    public override int MaximumPlayers => 2;

    [ExportGroup("Scene References")]
    [Export] public PoolBallRespawn PoolBallRespawn;
    [Export] public PoolGameTable PoolTable;
    [Export] public BallPlacementManager BallPlacementManager;
    [Export] public PoolSimulationRunner SimulationRunner;
    [Export] public PackedScene GameControllerScene;
    private GameModeHandler _gameModeHandler;
    private bool _runtimeReady;
    private int _setupVersion;
    private string _pendingReclaimNewId;
    private Dictionary _pendingReclaimContext;

    public Ball CueBall = null;
    public Array<Ball> Balls = new();

    public Dictionary<string, Array> BallsPocketedByPlayer = new();
    public Dictionary<string, int> ConsecutiveFoulsByPlayer = new();
    public int CurrentTargetBallIndex = 0;
    public bool PushOutAvailable;
    public bool PushOutChoicePending;
    public bool PushOutDeclared;
    public string PushOutShooterId;
    public GlobalCamera Camera;
    public bool IsSoloMatch { get; private set; }

    public override bool CountsWinsForRanking => !IsSoloMatch;

    [Signal]
    public delegate void HudStateUpdatedEventHandler();

    public override void _Ready()
    {
        SetProcess(false);
        BallPlacementManager.SimulationRunner = SimulationRunner;
        _gameModeHandler = GameModeHandler;
        MatchOver += OnMatchIsOver;
        MatchStarted += OnMatchStarts;
        PlayerRemovedFromMatch += OnPlayerRemovedFromMatch;
        PlayerReclaimed += OnPlayerReclaimed;
    }

    public override void _Process(double delta)
    {
        ProcessPendingReclaim();
    }

    public override void _ExitTree()
    {
        ClearPendingReclaim();
        base._ExitTree();
    }

    private void OnBallPocketed(Ball ball)
    {
        PoolTable.EmitBallPocketedSound();
    }

    public override void SetCamera(GlobalCamera camera)
    {
        Camera = camera;
        BallPlacementManager.Camera = camera;
    }

    public void ApplyHudUpdate(int targetBallIndex, string scoringPlayerId, Array scoredBalls)
    {
        CurrentTargetBallIndex = targetBallIndex;

        if (scoringPlayerId != null && scoredBalls != null && scoredBalls.Count > 0)
        {
            if (!BallsPocketedByPlayer.TryGetValue(scoringPlayerId, out var list))
            {
                list = new Array();
                BallsPocketedByPlayer[scoringPlayerId] = list;
            }

            foreach (var index in scoredBalls)
                list.Add(index);
        }

        EmitSignal(SignalName.HudStateUpdated);
    }

    public void ApplyFoulUpdate(string playerId, int foulCount)
    {
        if (string.IsNullOrEmpty(playerId))
            return;

        ConsecutiveFoulsByPlayer[playerId] = Mathf.Clamp(
            foulCount, 0, PoolTurnResolver.ConsecutiveFoulLossThreshold);
        EmitSignal(SignalName.HudStateUpdated);
    }

    public void ApplyPushOutState(bool available, bool choicePending, bool declared, string shooterId)
    {
        PushOutAvailable = available;
        PushOutChoicePending = choicePending;
        PushOutDeclared = declared;
        PushOutShooterId = shooterId ?? "";
        EmitSignal(SignalName.HudStateUpdated);
    }

    public override async void SetupMatch(Array players, string firstTurnOwner)
    {
        ClearPendingReclaim();
        PrepareMatch(players, firstTurnOwner);
        var setupVersion = ++_setupVersion;
        _runtimeReady = false;
        IsSoloMatch = players.Count == 1;
        BallsPocketedByPlayer.Clear();
        ConsecutiveFoulsByPlayer.Clear();
        CurrentTargetBallIndex = 0;
        PushOutAvailable = false;
        PushOutChoicePending = false;
        PushOutDeclared = false;
        PushOutShooterId = "";

        // Adopt the table's geometry before anything spawns: this also snaps the ball container
        // onto the cloth centre that table defines, so the rack lands in the right place.
        SimulationRunner.UseGeometry(PoolTable.Geometry);

        PoolBallRespawn.StartGame();

        var (cueBall, balls) = await PoolBallRespawn.WaitTableReady();
        if (setupVersion != _setupVersion || !IsMatchActive)
            return;

        CueBall = cueBall;
        Balls = balls;

        SimulationRunner.Setup(cueBall, balls);
        SignalUtil.ConnectGuarded(SimulationRunner, PoolSimulationRunner.SignalName.BallPocketed, new Callable(this, MethodName.OnBallPocketed));
        _gameModeHandler.Setup(this);
        _runtimeReady = true;

        EmitSignal(SignalName.MatchStarted, players, firstTurnOwner);

        if (!Multiplayer.IsServer())
            (_gameModeHandler.CurrentGameMode.TurnResolver as PoolTurnResolver)?.RequestFullState();

        ProcessPendingReclaim();
    }

    private void OnMatchStarts(Array playersIds, string firstTurnOwner)
    {
        var initialPlacementContext = new Dictionary
        {
            ["ball_replacement"] = 0,
            ["initial_break_placement"] = true,
        };

        foreach (var pIdVariant in playersIds)
        {
            var pId = (string)pIdVariant;
            var pNode = PlayerRegistry.Instance.GetPlayerById(pId);

            if (pNode != null)
                pNode.GameHandler.EquipGameController(GameControllerScene, this, Camera);
        }

        if (Player != null && Player.GameHandler.CurrentController != null)
            Player.GameHandler.CurrentController.ApplyControl(firstTurnOwner, initialPlacementContext);

        var resolver = _gameModeHandler?.CurrentGameMode?.TurnResolver as PoolTurnResolver;
        resolver?.BeginInitialBreakPlacement(firstTurnOwner);
    }

    private void OnMatchIsOver(string winner, Dictionary context)
    {
        _setupVersion++;
        _runtimeReady = false;
        ClearPendingReclaim();
        BallPlacementManager?.CancelPlacement();
        SimulationRunner?.CancelPlayback();

        // When the local player is the one who just left the match (surrender),
        // OnPlayerRemovedFromMatch below already released their controller by the time this
        // runs — CurrentController is already null here, not a bug to work around blindly.
        if (Player != null && Player.GameHandler.CurrentController != null)
        {
            Player.GameHandler.CurrentController.GiveControl();
            Player.GameHandler.UnequipCurrentController();
            Player.TakeControl();
        }

        if (Multiplayer.IsServer())
            PoolBallRespawn.ClearTable();
    }

    private void OnPlayerRemovedFromMatch(string playerId, Array turnOrder)
    {
        var leavingPlayer = PlayerRegistry.Instance.GetPlayerById(playerId);
        if (leavingPlayer == null || leavingPlayer.GameHandler.CurrentController == null)
            return;

        leavingPlayer.GameHandler.CurrentController.GiveControl();
        leavingPlayer.GameHandler.UnequipCurrentController();
        leavingPlayer.TakeControl();
    }

    private void OnPlayerReclaimed(string oldPlayerId, string newPlayerId, Array turnOrder, Dictionary context)
    {
        if (BallsPocketedByPlayer.TryGetValue(oldPlayerId, out var scoredBalls))
        {
            BallsPocketedByPlayer.Remove(oldPlayerId);
            BallsPocketedByPlayer[newPlayerId] = scoredBalls;
        }
        if (ConsecutiveFoulsByPlayer.TryGetValue(oldPlayerId, out var foulCount))
        {
            ConsecutiveFoulsByPlayer.Remove(oldPlayerId);
            ConsecutiveFoulsByPlayer[newPlayerId] = foulCount;
        }
        EmitSignal(SignalName.HudStateUpdated);

        QueuePendingReclaim(newPlayerId, context);
        ProcessPendingReclaim();
    }

    private bool TryApplyReclaimedPlayer(string newPlayerId, Dictionary context)
    {
        var registry = PlayerRegistry.Instance;
        if (registry == null || !registry.TryGetPlayerById(newPlayerId, out var newPlayer)
            || !IsInstanceValid(newPlayer))
            return false;

        AttachLocalReclaimedPlayer(newPlayerId, newPlayer);
        if (GameControllerScene == null || newPlayer.GameHandler == null)
            return false;

        // Reliable reclaim state can be replayed while the scene catches up. Re-equipping here
        // would destroy the live controller and could start a second placement continuation.
        if (newPlayer.GameHandler.CurrentTableGame == this
            && IsInstanceValid(newPlayer.GameHandler.CurrentController))
        {
            return true;
        }

        newPlayer.GameHandler.EquipGameController(GameControllerScene, this, Camera);

        // TableGame already transferred the cached turn identity without touching the old,
        // already-freed Player node.
        if (!IsTurnOwner(newPlayerId))
            return true;

        var resolver = _gameModeHandler?.CurrentGameMode?.TurnResolver as PoolTurnResolver;
        resolver?.ResumeReclaimedTurn(newPlayerId, context);
        newPlayer.GameHandler.CurrentController?.ApplyControl(newPlayerId, context);
        return true;
    }

    private void ProcessPendingReclaim()
    {
        if (string.IsNullOrEmpty(_pendingReclaimNewId))
        {
            SetProcess(false);
            return;
        }

        if (!IsMatchActive || !TurnOrder.Contains(_pendingReclaimNewId))
        {
            ClearPendingReclaim();
            return;
        }

        if (!_runtimeReady)
            return;

        var newId = _pendingReclaimNewId;
        var context = _pendingReclaimContext ?? new Dictionary();
        if (!TryApplyReclaimedPlayer(newId, context))
            return;

        _pendingReclaimNewId = null;
        _pendingReclaimContext = null;
        SetProcess(false);
        context.Dispose();
    }

    private void QueuePendingReclaim(string newPlayerId, Dictionary context)
    {
        ClearPendingReclaim();
        _pendingReclaimNewId = newPlayerId;
        _pendingReclaimContext = context?.Duplicate() ?? new Dictionary();
        SetProcess(true);
    }

    private void ClearPendingReclaim()
    {
        _pendingReclaimContext?.Dispose();
        _pendingReclaimNewId = null;
        _pendingReclaimContext = null;
        SetProcess(false);
    }

    internal bool HasPendingReclaimedController =>
        !string.IsNullOrEmpty(_pendingReclaimNewId);

    public Dictionary BuildPublicSnapshot()
    {
        var scoredPlayers = new System.Collections.Generic.List<string>();
        var scoredBalls = new System.Collections.Generic.List<int>();
        foreach (var entry in BallsPocketedByPlayer)
        {
            foreach (var ballVariant in entry.Value)
            {
                scoredPlayers.Add(entry.Key);
                scoredBalls.Add(ballVariant.AsInt32());
            }
        }

        var foulPlayers = new string[ConsecutiveFoulsByPlayer.Count];
        var foulCounts = new int[ConsecutiveFoulsByPlayer.Count];
        var index = 0;
        foreach (var entry in ConsecutiveFoulsByPlayer)
        {
            foulPlayers[index] = entry.Key;
            foulCounts[index] = entry.Value;
            index++;
        }

        return new Dictionary
        {
            ["target_ball"] = CurrentTargetBallIndex,
            ["scored_players"] = Variant.From(scoredPlayers.ToArray()),
            ["scored_ball_ids"] = Variant.From(scoredBalls.ToArray()),
            ["foul_players"] = Variant.From(foulPlayers),
            ["foul_counts"] = Variant.From(foulCounts),
            ["push_out_available"] = PushOutAvailable,
            ["push_out_choice_pending"] = PushOutChoicePending,
            ["push_out_declared"] = PushOutDeclared,
            ["push_out_shooter"] = PushOutShooterId ?? "",
        };
    }

    public void ApplyPublicSnapshot(Dictionary context)
    {
        if (context == null || !context.ContainsKey("target_ball"))
            return;

        CurrentTargetBallIndex = context["target_ball"].AsInt32();
        BallsPocketedByPlayer.Clear();
        var scoredPlayers = context["scored_players"].AsStringArray();
        var scoredBalls = context["scored_ball_ids"].AsInt32Array();
        for (var i = 0; i < scoredPlayers.Length && i < scoredBalls.Length; i++)
        {
            if (!BallsPocketedByPlayer.TryGetValue(scoredPlayers[i], out var balls))
            {
                balls = new Array();
                BallsPocketedByPlayer[scoredPlayers[i]] = balls;
            }
            balls.Add(scoredBalls[i]);
        }

        ConsecutiveFoulsByPlayer.Clear();
        var foulPlayers = context["foul_players"].AsStringArray();
        var foulCounts = context["foul_counts"].AsInt32Array();
        for (var i = 0; i < foulPlayers.Length && i < foulCounts.Length; i++)
            ConsecutiveFoulsByPlayer[foulPlayers[i]] = foulCounts[i];

        PushOutAvailable = context["push_out_available"].AsBool();
        PushOutChoicePending = context["push_out_choice_pending"].AsBool();
        PushOutDeclared = context["push_out_declared"].AsBool();
        PushOutShooterId = context["push_out_shooter"].AsString();
        EmitSignal(SignalName.HudStateUpdated);
    }
}
