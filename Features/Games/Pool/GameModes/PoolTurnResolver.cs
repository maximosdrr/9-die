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
    public const int ConsecutiveFoulLossThreshold = 5;

    [Export] public TurnRuler TurnRuler;

    public PoolGame PoolGame;
    public Ball CueBall;

    public Dictionary<int, Ball> BallsInGame = new();

    private readonly System.Collections.Generic.Dictionary<string, int> _consecutiveFouls = new();
    private bool _isBreakShot = true;
    private bool _pushOutAvailable;
    private bool _pushOutDeclaredForShot;
    private bool _awaitingPushOutChoice;
    private string _pushOutShooterId = "";
    private bool _initialBreakPlacementPending = true;

    public bool IsShotBlocked => _awaitingPushOutChoice || _initialBreakPlacementPending;

    public override void Setup(TableGame tableGame)
    {
        PoolGame = (PoolGame)tableGame;
        CueBall = PoolGame.CueBall;

        BallsInGame.Clear();
        _consecutiveFouls.Clear();
        _isBreakShot = true;
        _initialBreakPlacementPending = true;
        ResetPushOutState();

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
        _consecutiveFouls.Clear();
        _isBreakShot = true;
        _initialBreakPlacementPending = true;
        ResetPushOutState();
        CueBall = null;
        PoolGame = null;
    }

    private void ConnectSignals()
    {
        if (PoolGame == null)
            return;

        SignalUtil.ConnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnStart));
        SignalUtil.ConnectGuarded(PoolGame.SimulationRunner, PoolSimulationRunner.SignalName.ShotFinished, new Callable(this, MethodName.OnShotFinished));
        SignalUtil.ConnectGuarded(PoolGame.BallPlacementManager,
            BallPlacementManager.SignalName.PlacementCommitted,
            new Callable(this, MethodName.OnPlacementCommitted));
    }

    private void DisconnectSignals()
    {
        if (PoolGame == null)
            return;

        SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnStart));
        SignalUtil.DisconnectGuarded(PoolGame.SimulationRunner, PoolSimulationRunner.SignalName.ShotFinished, new Callable(this, MethodName.OnShotFinished));
        SignalUtil.DisconnectGuarded(PoolGame.BallPlacementManager,
            BallPlacementManager.SignalName.PlacementCommitted,
            new Callable(this, MethodName.OnPlacementCommitted));
    }

    public void BeginInitialBreakPlacement(string ownerId)
    {
        if (PoolGame == null || !int.TryParse(ownerId, out var ownerPeerId))
            return;

        _initialBreakPlacementPending = true;
        var headStringZ = PoolGame.PoolBallRespawn.HeadSpot.Z;

        if (Multiplayer.IsServer())
        {
            PoolGame.BallPlacementManager.AuthorizePlacement(ownerPeerId, CueBall,
                BallPlacementManager.PlacementRegion.BehindHeadString, headStringZ);
        }

        if (Multiplayer.GetUniqueId() == ownerPeerId)
        {
            _ = HandleBallReplacement(CueBall,
                BallPlacementManager.PlacementRegion.BehindHeadString, headStringZ);
        }
    }

    private void OnPlacementCommitted()
    {
        _initialBreakPlacementPending = false;
    }

    private void OnTurnStart(string ownerId, Dictionary context)
    {
        if (!context.ContainsKey("ball_replacement"))
            return;

        var ballIndex = (int)context["ball_replacement"];
        var target = ballIndex == 0 ? CueBall : BallsInGame[ballIndex];

        var initialBreakPlacement = context.TryGetValue(
            "initial_break_placement", out var initialVariant) && initialVariant.AsBool();
        var region = initialBreakPlacement
            ? BallPlacementManager.PlacementRegion.BehindHeadString
            : BallPlacementManager.PlacementRegion.FullTable;
        var headStringZ = initialBreakPlacement ? PoolGame.PoolBallRespawn.HeadSpot.Z : 0.0f;

        if (initialBreakPlacement)
            _initialBreakPlacementPending = true;

        if (Multiplayer.IsServer())
            PoolGame.BallPlacementManager.AuthorizePlacement(
                int.Parse(ownerId), target, region, headStringZ);

        if (Multiplayer.GetUniqueId() != int.Parse(ownerId))
            return;

        _ = HandleBallReplacement(target, region, headStringZ);
    }

    private async Task HandleBallReplacement(Ball target,
        BallPlacementManager.PlacementRegion region = BallPlacementManager.PlacementRegion.FullTable,
        float headStringZ = 0.0f)
    {
        var balls = new Array<Ball>(BallsInGame.Values);

        PoolGame.BallPlacementManager.StartPlacement(target, balls, region, headStringZ);
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

        // The nine is always spotted when made or driven off on the break, including a legal
        // break. On later legal shots it remains the game-winning ball.
        if (context.IsBreakShot && context.IsLegalBreak)
            RespotGoldenBallIfNeeded(ballsScoredThisTurn, context.BallsOffTable);

        _pushOutAvailable = !PoolGame.IsSoloMatch && context.IsBreakShot && context.IsLegalBreak;

        if (PoolGame.IsSoloMatch)
        {
            if (action == TurnRuler.Actions.CallCueBallReplacement)
                RespotGoldenBallIfNeeded(ballsScoredThisTurn, context.BallsOffTable);

            _isBreakShot = false;
            _pushOutDeclaredForShot = false;
            _consecutiveFouls.Remove(scoringPlayerId);
            ApplySoloTurnAction(AdaptActionForSolo(action, context), scoringPlayerId,
                ballsScoredThisTurn);
            return;
        }

        var committedFoul = action == TurnRuler.Actions.CallCueBallReplacement;
        if (committedFoul)
        {
            var count = _consecutiveFouls.TryGetValue(scoringPlayerId, out var previous)
                ? previous + 1
                : 1;
            _consecutiveFouls[scoringPlayerId] = count;

            if (IsFatalFoulCount(count))
                action = TurnRuler.Actions.EndGameFatalFoul;
        }
        else
        {
            _consecutiveFouls[scoringPlayerId] = 0;
        }

        _isBreakShot = false;
        _pushOutDeclaredForShot = false;

        ApplyTurnAction(action, scoringPlayerId, ballsScoredThisTurn, context.BallsOffTable);
    }

    internal static TurnRuler.Actions AdaptActionForSolo(
        TurnRuler.Actions competitiveAction, TurnContext context)
    {
        if (competitiveAction == TurnRuler.Actions.EndGamePlayerWin)
            return competitiveAction;

        return CueBallNeedsPlacement(context)
            ? TurnRuler.Actions.CallCueBallReplacement
            : TurnRuler.Actions.ExtendTurn;
    }

    internal static bool IsFatalFoulCount(int foulCount)
    {
        return foulCount >= ConsecutiveFoulLossThreshold;
    }

    private static bool CueBallNeedsPlacement(TurnContext context)
    {
        if (context.BallsScored.ContainsKey(CueBallId))
            return true;

        if (context.BallsOffTable == null)
            return false;

        foreach (var ball in context.BallsOffTable)
        {
            if (GodotObject.IsInstanceValid(ball) && ball.Index == CueBallId)
                return true;
        }

        return false;
    }

    private void ApplySoloTurnAction(TurnRuler.Actions action, string scoringPlayerId,
        Dictionary<int, Ball> ballsScoredThisTurn)
    {
        if (action == TurnRuler.Actions.EndGamePlayerWin)
        {
            PoolGame.ApplyMatchOver(scoringPlayerId, new Dictionary { ["reason"] = "win" });
            Reset();
            return;
        }

        var context = BuildHudContext(scoringPlayerId, ballsScoredThisTurn);
        if (action == TurnRuler.Actions.CallCueBallReplacement)
            context["ball_replacement"] = CueBallId;

        PoolGame.CallExtendCurrentTurn(context);
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

        var numberedBallsPocketed = 0;
        foreach (var index in ballsScored.Keys)
        {
            if (index != CueBallId)
                numberedBallsPocketed++;
        }

        var objectBallsAtRail = result.CountDistinctBallsAtCushion(CueBallId);
        var breakHasNoGeneralFoul = !ballsScored.ContainsKey(CueBallId)
                                    && ballsOffTable.Count == 0
                                    && targetBall != null
                                    && result.FirstBallContacted(CueBallId) == targetBall.Index;
        var legalBreak = !_isBreakShot || (breakHasNoGeneralFoul
            && (numberedBallsPocketed > 0 || objectBallsAtRail >= 4));

        return new TurnContext
        {
            BallsScored = ballsScored,
            FirstBallTouched = runner.FindBall(result.FirstBallContacted(CueBallId)),
            BallsOffTable = ballsOffTable,
            TargetBall = targetBall,
            CurrentBallsRemaining = remaining,
            AnyRailContact = result.AnyCushionContactAfterFirstBallContact(CueBallId),
            IsBreakShot = _isBreakShot,
            IsLegalBreak = legalBreak,
            ObjectBallsDrivenToRail = objectBallsAtRail,
            IsPushOut = _pushOutDeclaredForShot,
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

    private void ApplyTurnAction(TurnRuler.Actions action, string scoringPlayerId,
        Dictionary<int, Ball> ballsScoredThisTurn, Array<Ball> ballsOffTable)
    {
        switch (action)
        {
            case TurnRuler.Actions.None:
                GD.PushWarning("Regra não produziu decisão; o turno ficará como está.");
                break;

            case TurnRuler.Actions.CallNextTurn:
                PoolGame.CallNextTurn(BuildHudContext(scoringPlayerId, ballsScoredThisTurn));
                break;

            case TurnRuler.Actions.ExtendTurn:
                PoolGame.CallExtendCurrentTurn(BuildHudContext(scoringPlayerId, ballsScoredThisTurn));
                break;

            case TurnRuler.Actions.CallPushOutChoice:
                RespotGoldenBallIfNeeded(ballsScoredThisTurn, ballsOffTable);
                _pushOutAvailable = false;
                _awaitingPushOutChoice = true;
                _pushOutShooterId = scoringPlayerId;
                var pushOutContext = BuildHudContext(scoringPlayerId, ballsScoredThisTurn);
                pushOutContext["push_out_choice_pending"] = true;
                pushOutContext["push_out_shooter"] = scoringPlayerId;
                PoolGame.CallNextTurn(pushOutContext);
                break;

            case TurnRuler.Actions.CallCueBallReplacement:
                RespotGoldenBallIfNeeded(ballsScoredThisTurn, ballsOffTable);
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
            ["foul_count"] = _consecutiveFouls.TryGetValue(scoringPlayerId, out var fouls) ? fouls : 0,
            ["foul_player"] = scoringPlayerId,
            ["push_out_available"] = _pushOutAvailable,
            ["push_out_choice_pending"] = _awaitingPushOutChoice,
            ["push_out_declared"] = _pushOutDeclaredForShot,
            ["push_out_shooter"] = _pushOutShooterId,
        };
    }

    public override void HandleNewTurnContext(Dictionary context)
    {
        ApplyHudContext(context);
    }

    public override Dictionary BuildHandoffContext(string outgoingPlayerId)
    {
        if (PoolGame != null && PoolGame.BallPlacementManager.IsPlacementPendingFor(outgoingPlayerId))
        {
            var context = new Dictionary { ["ball_replacement"] = 0 };
            if (_initialBreakPlacementPending)
                context["initial_break_placement"] = true;
            return context;
        }

        return new Dictionary();
    }

    public override void HandleTurnExtensionContext(Dictionary context)
    {
        ApplyHudContext(context);

        if (context.ContainsKey("ball_replacement") && PoolGame?.TurnOwner != null)
            OnTurnStart((string)PoolGame.TurnOwner.Name, context);
    }

    private void ApplyHudContext(Dictionary context)
    {
        if (PoolGame == null)
            return;

        var targetBallIndex = context.TryGetValue("target_ball", out var targetVariant) ? targetVariant.AsInt32() : PoolGame.CurrentTargetBallIndex;
        var scoringPlayerId = context.TryGetValue("scoring_player", out var playerVariant) ? playerVariant.AsString() : null;
        var scoredBalls = context.TryGetValue("scored_balls", out var ballsVariant) ? ballsVariant.AsGodotArray() : null;
        var foulPlayerId = context.TryGetValue("foul_player", out var foulPlayerVariant) ? foulPlayerVariant.AsString() : null;
        var foulCount = context.TryGetValue("foul_count", out var foulCountVariant) ? foulCountVariant.AsInt32() : 0;
        var pushOutAvailable = context.TryGetValue("push_out_available", out var availableVariant) && availableVariant.AsBool();
        var pushOutChoicePending = context.TryGetValue("push_out_choice_pending", out var pendingVariant) && pendingVariant.AsBool();
        var pushOutDeclared = context.TryGetValue("push_out_declared", out var declaredVariant) && declaredVariant.AsBool();
        var pushOutShooter = context.TryGetValue("push_out_shooter", out var shooterVariant) ? shooterVariant.AsString() : "";

        PoolGame.ApplyHudUpdate(targetBallIndex, string.IsNullOrEmpty(scoringPlayerId) ? null : scoringPlayerId, scoredBalls);
        PoolGame.ApplyFoulUpdate(foulPlayerId, foulCount);
        PoolGame.ApplyPushOutState(pushOutAvailable, pushOutChoicePending, pushOutDeclared, pushOutShooter);
    }

    public void RequestDeclarePushOut()
    {
        if (Multiplayer.IsServer())
            TryDeclarePushOut(Multiplayer.GetUniqueId());
        else
            RpcId(1, MethodName.DeclarePushOutOnServer);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void DeclarePushOutOnServer()
    {
        if (Multiplayer.IsServer())
            TryDeclarePushOut(Multiplayer.GetRemoteSenderId());
    }

    private void TryDeclarePushOut(int requesterId)
    {
        if (PoolGame?.IsSoloMatch != false || !_pushOutAvailable
            || _awaitingPushOutChoice || PoolGame.TurnOwner == null
            || (string)PoolGame.TurnOwner.Name != requesterId.ToString()
            || PoolGame.SimulationRunner.IsPlaying)
            return;

        _pushOutAvailable = false;
        _pushOutDeclaredForShot = true;
        Rpc(MethodName.SyncPushOutState, false, false, true, requesterId.ToString());
    }

    public void RequestPushOutChoice(bool passBack)
    {
        if (Multiplayer.IsServer())
            TryResolvePushOutChoice(Multiplayer.GetUniqueId(), passBack);
        else
            RpcId(1, MethodName.ResolvePushOutChoiceOnServer, passBack);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ResolvePushOutChoiceOnServer(bool passBack)
    {
        if (Multiplayer.IsServer())
            TryResolvePushOutChoice(Multiplayer.GetRemoteSenderId(), passBack);
    }

    private void TryResolvePushOutChoice(int requesterId, bool passBack)
    {
        if (PoolGame?.IsSoloMatch != false || !_awaitingPushOutChoice || PoolGame.TurnOwner == null
            || (string)PoolGame.TurnOwner.Name != requesterId.ToString())
            return;

        _awaitingPushOutChoice = false;
        var context = BuildHudContext(_pushOutShooterId,
            new Dictionary<int, Ball>());
        context["push_out_choice_pending"] = false;
        context["push_out_choice_resolved"] = true;
        context["push_out_shooter"] = "";
        _pushOutShooterId = "";

        if (passBack)
            PoolGame.CallNextTurn(context);
        else
            PoolGame.CallExtendCurrentTurn(context);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncPushOutState(bool available, bool choicePending, bool declared, string shooterId)
    {
        PoolGame?.ApplyPushOutState(available, choicePending, declared, shooterId);
    }

    private void ResetPushOutState()
    {
        _pushOutAvailable = false;
        _pushOutDeclaredForShot = false;
        _awaitingPushOutChoice = false;
        _pushOutShooterId = "";
    }

    private void RespotGoldenBallIfNeeded(Dictionary<int, Ball> ballsScoredThisTurn, Array<Ball> contextBallsOffTable)
    {
        ballsScoredThisTurn.TryGetValue(9, out var goldenBall);
        if (!IsInstanceValid(goldenBall) && contextBallsOffTable != null)
        {
            foreach (var ball in contextBallsOffTable)
            {
                if (ball.Index == 9)
                {
                    goldenBall = ball;
                    break;
                }
            }
        }

        if (!IsInstanceValid(goldenBall))
            return;

        var footSpot = PoolGame.PoolBallRespawn.FootSpot;
        var preferred = new Vector2(footSpot.X, footSpot.Z);
        if (!PoolGame.SimulationRunner.TryFindNearestFreeSpot(preferred, goldenBall, out var freeSpot))
        {
            GD.PushError("Não foi possível encontrar uma posição livre para recolocar a bola 9.");
            return;
        }

        PoolGame.SimulationRunner.PlaceBall(goldenBall, freeSpot);

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
