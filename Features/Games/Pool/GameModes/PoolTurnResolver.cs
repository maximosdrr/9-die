using Godot;
using Godot.Collections;
using Pool.Simulation;
using System.Threading.Tasks;

/// <summary>
/// Turns the outcome of a shot into a turn decision.
///
/// The facts come from the simulation's event timeline rather than from physics callbacks. That
/// removed a whole family of failures at once: there is no longer an await that can hang when no
/// ball ever moves, no per-ball "has it stopped" edge detection to miss, and no contact limit to
/// silently swallow a rail hit during a break. The timeline is complete and ordered by
/// construction.
/// </summary>
[GlobalClass]
public partial class PoolTurnResolver : TurnResolver
{
    [Export] public TurnRuler TurnRuler;

    public PoolGame PoolGame;
    public Ball CueBall;

    public Dictionary<int, Ball> BallsInGame = new();

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

        BallsInGame.Clear();
        CueBall = null;
        PoolGame = null;
    }

    private void ConnectSignals()
    {
        if (PoolGame == null)
            return;

        SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnStart));
        SignalUtil.ConnectGuarded(PoolGame.SimulationRunner, PoolSimulationRunner.SignalName.ShotFinished, new Callable(this, MethodName.OnShotFinished));
    }

    private void DisconnectSignals()
    {
        if (PoolGame == null)
            return;

        SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnStart));
        SignalUtil.DisconnectGuarded(PoolGame.SimulationRunner, PoolSimulationRunner.SignalName.ShotFinished, new Callable(this, MethodName.OnShotFinished));
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

    private void OnShotFinished()
    {
        // Only the server rules on a turn; every peer plays the same shot back but the decision
        // is made once and broadcast.
        if (!Multiplayer.IsServer())
            return;

        var result = PoolGame.SimulationRunner.LastShot;
        if (result == null)
            return;

        var context = GenerateTurnContext(result);
        var action = TurnRuler.Rule(context);
        var scoringPlayerId = (string)PoolGame.TurnOwner.Name;
        var ballsScoredThisTurn = context.BallsScored;

        BallsInGame = context.CurrentBallsRemaining;

        ApplyTurnAction(action, scoringPlayerId, ballsScoredThisTurn);
    }

    /// <summary>
    /// Reads the whole turn out of the shot's event timeline. Every fact the ruler needs is a
    /// query over an ordered list, which is why none of the old failure modes survive: a shot
    /// that moves nothing still produces a (empty) timeline and resolves, and "first ball
    /// touched" is the first contact in the list rather than whichever Area3D happened to fire.
    /// </summary>
    private TurnContext GenerateTurnContext(ShotResult result)
    {
        var runner = PoolGame.SimulationRunner;

        var ballsScored = new Dictionary<int, Ball>();
        var ballsOffTable = new Array<Ball>();

        foreach (var shotEvent in result.Events)
        {
            var ball = runner.FindBall(shotEvent.BallId);
            if (ball == null)
                continue;

            if (shotEvent.Type == ShotEventType.BallPocketed)
                ballsScored[ball.Index] = ball;
            else if (shotEvent.Type == ShotEventType.BallOffTable && !ballsOffTable.Contains(ball))
                ballsOffTable.Add(ball);
        }

        var targetBall = FindTargetBall();

        var remaining = new Dictionary<int, Ball>();
        foreach (var kvp in BallsInGame)
        {
            if (!ballsScored.ContainsKey(kvp.Key) && !ballsOffTable.Contains(kvp.Value))
                remaining[kvp.Key] = kvp.Value;
        }

        return new TurnContext
        {
            BallsScored = ballsScored,
            FirstBallTouched = runner.FindBall(result.FirstBallContacted(CueBallId)),
            BallsOffTable = ballsOffTable,
            TargetBall = targetBall,
            CurrentBallsRemaining = remaining,
            AnyRailContact = result.AnyCushionContact(),
        };
    }

    private const int CueBallId = 0;

    private Ball FindTargetBall()
    {
        if (BallsInGame.Count == 0)
            return null;

        var lowest = System.Linq.Enumerable.Min(BallsInGame.Keys);
        return BallsInGame.TryGetValue(lowest, out var ball) ? ball : null;
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
                RespotGoldenBallIfScored(ballsScoredThisTurn);
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

    private void RespotGoldenBallIfScored(Dictionary<int, Ball> ballsScoredThisTurn)
    {
        if (!ballsScoredThisTurn.TryGetValue(9, out var goldenBall) || !IsInstanceValid(goldenBall))
            return;

        var footSpot = PoolGame.PoolBallRespawn.FootSpot;
        PoolGame.SimulationRunner.PlaceBall(goldenBall, new Vector2(footSpot.X, footSpot.Z));

        // The re-spot happens on the server only, so push the layout out now rather than letting
        // clients show the ball in the old place until the next shot's snapshot corrects it.
        PoolGame.SimulationRunner.BroadcastState();

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
