using Godot;
using Godot.Collections;
using System.Threading.Tasks;

[GlobalClass]
public partial class PoolTurnResolver : TurnResolver
{
    [Export] public TurnRuler TurnRuler;

    public PoolGame PoolGame;
    public Ball CueBall;

    public Dictionary<int, Ball> BallsScored = new();
    public Dictionary<int, Ball> BallsInGame = new();
    public Array<Ball> BallsOffTableList = new();
    public Ball FirstBallHit = null;
    public bool AnyRailContact = false;

    public override void Setup(TableGame tableGame)
    {
        PoolGame = (PoolGame)tableGame;
        CueBall = PoolGame.CueBall;

        BallsInGame.Clear();

        foreach (var ball in PoolGame.Balls)
            BallsInGame[ball.Index] = ball;

        var initialTarget = BallsInGame.Keys.Count > 0 ? System.Linq.Enumerable.Min(BallsInGame.Keys) : 0;
        PoolGame.ApplyHudUpdate(initialTarget, null, null);

        ConnectSignals();
    }

    public void Reset()
    {
        DisconnectSignals();

        BallsScored.Clear();
        BallsInGame.Clear();
        BallsOffTableList.Clear();
        FirstBallHit = null;

        CueBall = null;
        PoolGame = null;
    }

    private void ConnectSignals()
    {
        if (PoolGame == null)
            return;

        SignalUtil.ConnectGuarded(PoolGame.CueBall, Ball.SignalName.Striked, new Callable(this, MethodName.OnStrike));
        SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnStart));

        // Potting and driven-off balls now come from the simulated event timeline rather than
        // from Area3D triggers on the table. The old detectors relied on a ball physically
        // falling through a gap in the rails, which stopped being how any of this works.
        SignalUtil.ConnectGuarded(PoolGame.SimulationRunner, PoolSimulationRunner.SignalName.BallPocketed, new Callable(this, MethodName.OnBallPocketed));
        SignalUtil.ConnectGuarded(PoolGame.SimulationRunner, PoolSimulationRunner.SignalName.BallDrivenOffTable, new Callable(this, MethodName.OnBallFellOff));
    }

    private void DisconnectSignals()
    {
        if (PoolGame == null)
            return;

        SignalUtil.DisconnectGuarded(PoolGame.CueBall, Ball.SignalName.Striked, new Callable(this, MethodName.OnStrike));
        SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnStart));
        SignalUtil.DisconnectGuarded(PoolGame.SimulationRunner, PoolSimulationRunner.SignalName.BallPocketed, new Callable(this, MethodName.OnBallPocketed));
        SignalUtil.DisconnectGuarded(PoolGame.SimulationRunner, PoolSimulationRunner.SignalName.BallDrivenOffTable, new Callable(this, MethodName.OnBallFellOff));
    }

    private void OnBallPocketed(Ball ball)
    {
        BallsScored[ball.Index] = ball;
    }

    private void OnBallFellOff(Ball ball)
    {
        if (!BallsOffTableList.Contains(ball))
            BallsOffTableList.Add(ball);
    }

    private void OnCueBallContact(Ball ball)
    {
        if (FirstBallHit == null)
            FirstBallHit = ball;
    }

    private void OnAnyBallTouchedRail()
    {
        AnyRailContact = true;
    }

    private void OnTurnStart(string ownerId, Dictionary context)
    {
        if (!context.ContainsKey("ball_replacement"))
            return;

        var ballIndex = (int)context["ball_replacement"];
        var target = ballIndex == 0 ? CueBall : BallsInGame[ballIndex];

        if (Multiplayer.IsServer())
            PoolGame.BallPlacementManager.AuthorizePlacement(int.Parse(ownerId), target);

        if (Multiplayer.GetUniqueId() != int.Parse(ownerId))
            return;

        _ = HandleBallReplacement(target);
    }

    private async Task HandleBallReplacement(Ball target)
    {
        var balls = new Array<Ball>(BallsInGame.Values);

        PoolGame.BallPlacementManager.StartPlacement(target, balls);
        await ToSignal(PoolGame.BallPlacementManager, BallPlacementManager.SignalName.PlacementFinished);
    }

    private async void OnStrike()
    {
        ResetTurnState();

        SignalUtil.ConnectGuarded(CueBall, Ball.SignalName.BallContacted, new Callable(this, MethodName.OnCueBallContact));
        ConnectRailListeners();
        await ToSignal(PoolGame.BallsMovementMonitor, BallsMovementMonitor.SignalName.BallsStopped);
        SignalUtil.DisconnectGuarded(CueBall, Ball.SignalName.BallContacted, new Callable(this, MethodName.OnCueBallContact));
        DisconnectRailListeners();

        var context = GenerateTurnContext();
        var action = TurnRuler.Rule(context);
        var scoringPlayerId = (string)PoolGame.TurnOwner.Name;
        var ballsScoredThisTurn = context.BallsScored;

        BallsInGame = context.CurrentBallsRemaining;

        ApplyTurnAction(action, scoringPlayerId, ballsScoredThisTurn);
    }

    private void ResetTurnState()
    {
        BallsScored.Clear();
        BallsOffTableList.Clear();
        FirstBallHit = null;
        AnyRailContact = false;
    }

    private void ConnectRailListeners()
    {
        SignalUtil.ConnectGuarded(CueBall, Ball.SignalName.TouchedRail, new Callable(this, MethodName.OnAnyBallTouchedRail));

        foreach (var kvp in BallsInGame)
            SignalUtil.ConnectGuarded(kvp.Value, Ball.SignalName.TouchedRail, new Callable(this, MethodName.OnAnyBallTouchedRail));
    }

    private void DisconnectRailListeners()
    {
        SignalUtil.DisconnectGuarded(CueBall, Ball.SignalName.TouchedRail, new Callable(this, MethodName.OnAnyBallTouchedRail));

        foreach (var kvp in BallsInGame)
            SignalUtil.DisconnectGuarded(kvp.Value, Ball.SignalName.TouchedRail, new Callable(this, MethodName.OnAnyBallTouchedRail));
    }

    private TurnContext GenerateTurnContext()
    {
        var currentBallsRemaining = new Dictionary<int, Ball>();
        foreach (var kvp in BallsInGame)
            currentBallsRemaining[kvp.Key] = kvp.Value;

        if (currentBallsRemaining.Count == 0)
            GD.PushError("No ball registered yet!");

        var targetBallIndex = BallsInGame.Keys.Count > 0 ? System.Linq.Enumerable.Min(BallsInGame.Keys) : 0;
        var targetBall = BallsInGame.TryGetValue(targetBallIndex, out var tb) ? tb : null;

        foreach (var scoredIndex in BallsScored.Keys)
            currentBallsRemaining.Remove(scoredIndex);

        foreach (var ball in BallsOffTableList)
            currentBallsRemaining.Remove(ball.Index);

        var ballsScored = new Dictionary<int, Ball>();
        foreach (var kvp in BallsScored)
            ballsScored[kvp.Key] = kvp.Value;

        return new TurnContext
        {
            BallsScored = ballsScored,
            FirstBallTouched = FirstBallHit,
            BallsOffTable = new Array<Ball>(BallsOffTableList),
            TargetBall = targetBall,
            CurrentBallsRemaining = currentBallsRemaining,
            AnyRailContact = AnyRailContact,
        };
    }

    private void ApplyTurnAction(TurnRuler.Actions action, string scoringPlayerId, Dictionary<int, Ball> ballsScoredThisTurn)
    {
        switch (action)
        {
            case TurnRuler.Actions.CallNextTurn:
                PoolGame.CallNextTurn(BuildHudContext(scoringPlayerId, ballsScoredThisTurn));
                break;

            case TurnRuler.Actions.ExtendTurn:
                PoolGame.CallExtendCurrentTurn(BuildHudContext(scoringPlayerId, ballsScoredThisTurn));
                break;

            case TurnRuler.Actions.CallCueBallReplacement:
                RespotGoldenBallIfScored();
                var replacementContext = BuildHudContext(scoringPlayerId, ballsScoredThisTurn);
                replacementContext["ball_replacement"] = 0;
                PoolGame.CallNextTurn(replacementContext);
                break;

            case TurnRuler.Actions.EndGameFatalFoul:
                PoolGame.ApplyMatchOver(GetOpponentId(), new Dictionary { ["reason"] = "fatal_foul" });
                Reset();
                break;

            case TurnRuler.Actions.EndGamePlayerWin:
                PoolGame.ApplyMatchOver((string)PoolGame.TurnOwner.Name, new Dictionary { ["reason"] = "win" });
                Reset();
                break;
        }
    }

    private Dictionary BuildHudContext(string scoringPlayerId, Dictionary<int, Ball> ballsScoredThisTurn)
    {
        var scoredIndices = new Array();
        foreach (var index in ballsScoredThisTurn.Keys)
        {
            if (index != 0 && !BallsInGame.ContainsKey(index))
                scoredIndices.Add(index);
        }

        var targetBallIndex = BallsInGame.Keys.Count > 0 ? System.Linq.Enumerable.Min(BallsInGame.Keys) : 0;

        return new Dictionary
        {
            ["scoring_player"] = scoringPlayerId,
            ["scored_balls"] = scoredIndices,
            ["target_ball"] = targetBallIndex,
        };
    }

    public override void HandleNewTurnContext(Dictionary context)
    {
        ApplyHudContext(context);
    }

    public override Dictionary BuildHandoffContext(string outgoingPlayerId)
    {
        if (PoolGame != null && PoolGame.BallPlacementManager.IsPlacementPendingFor(outgoingPlayerId))
            return new Dictionary { ["ball_replacement"] = 0 };

        return new Dictionary();
    }

    public override void HandleTurnExtensionContext(Dictionary context)
    {
        ApplyHudContext(context);
    }

    private void ApplyHudContext(Dictionary context)
    {
        if (PoolGame == null)
            return;

        var targetBallIndex = context.TryGetValue("target_ball", out var targetVariant) ? targetVariant.AsInt32() : PoolGame.CurrentTargetBallIndex;
        var scoringPlayerId = context.TryGetValue("scoring_player", out var playerVariant) ? playerVariant.AsString() : null;
        var scoredBalls = context.TryGetValue("scored_balls", out var ballsVariant) ? ballsVariant.AsGodotArray() : null;

        PoolGame.ApplyHudUpdate(targetBallIndex, string.IsNullOrEmpty(scoringPlayerId) ? null : scoringPlayerId, scoredBalls);
    }

    private void RespotGoldenBallIfScored()
    {
        if (!BallsScored.TryGetValue(9, out var goldenBall) || !IsInstanceValid(goldenBall))
            return;

        var footSpot = PoolGame.PoolBallRespawn.FootSpot;
        PoolGame.SimulationRunner.PlaceBall(goldenBall, new Vector2(footSpot.X, footSpot.Z));

        BallsInGame[9] = goldenBall;
    }

    private string GetOpponentId()
    {
        var currentId = (string)PoolGame.TurnOwner.Name;
        var currentIndex = PoolGame.TurnOrder.IndexOf(currentId);

        if (currentIndex == -1)
            return currentId;

        var nextIndex = (currentIndex + 1) % PoolGame.TurnOrder.Count;
        return (string)PoolGame.TurnOrder[nextIndex];
    }
}
