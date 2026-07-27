using Godot;
using Godot.Collections;
using System.Threading.Tasks;

[GlobalClass]
public partial class GoldenNineTurnResolver : TurnResolver
{
    [Export] public TurnRuler TurnRuler;
    [Export] public OffTableMonitor OffTableMonitor;
    [Export] public BallPlacementManager BallPlacementManager;

    public GoldenNineCueBallContactListener CueBallContactListener;
    public GoldenNineScoreListener ScoreListener;
    public GoldenNineBallFellOffListener BallFellOffListener;

    public PoolGame PoolGame;
    public Ball CueBall;

    public Dictionary<int, Ball> BallsScored = new();
    public Dictionary<int, Ball> BallsInGame = new();
    public Array<Ball> BallsOffTableList = new();
    public Ball FirstBallHit = null;

    public override void _Ready()
    {
        CueBallContactListener = GetNode<GoldenNineCueBallContactListener>("GoldenNineCueBallContactListener");
        ScoreListener = GetNode<GoldenNineScoreListener>("GoldenNineScoreListener");
        BallFellOffListener = GetNode<GoldenNineBallFellOffListener>("GoldenNineBallFellOffListener");
    }

    public override void Setup(TableGame tableGame)
    {
        PoolGame = (PoolGame)tableGame;
        CueBall = PoolGame.CueBall;

        ScoreListener.Setup(this);
        CueBallContactListener.Setup(this);
        BallFellOffListener.Setup(this);

        BallsInGame.Clear();

        foreach (var ball in PoolGame.Balls)
            BallsInGame[ball.Index] = ball;

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
    }

    private void DisconnectSignals()
    {
        if (PoolGame == null)
            return;

        SignalUtil.DisconnectGuarded(PoolGame.CueBall, Ball.SignalName.Striked, new Callable(this, MethodName.OnStrike));
        SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnStart));
    }

    private void OnTurnStart(string ownerId, Dictionary context)
    {
        if (Multiplayer.GetUniqueId() != int.Parse(ownerId))
            return;

        if (context.ContainsKey("ball_replacement"))
            _ = HandleBallReplacement(context);
    }

    private async Task HandleBallReplacement(Dictionary context)
    {
        var ballIndex = (int)context["ball_replacement"];
        var target = ballIndex == 0 ? CueBall : BallsInGame[ballIndex];
        var balls = new Array<Ball>(BallsInGame.Values);

        BallPlacementManager.StartPlacement(target, balls);
        await ToSignal(BallPlacementManager, BallPlacementManager.SignalName.PlacementFinished);
    }

    private async void OnStrike()
    {
        ResetTurnState();

        CueBallContactListener.StartListeningCollisions();
        await ToSignal(PoolGame.BallsMovementMonitor, BallsMovementMonitor.SignalName.BallsStopped);
        CueBallContactListener.StopListeningCollisions();

        var context = GenerateTurnContext();
        var action = TurnRuler.Rule(context);

        var currentBallsRemainingDict = (Dictionary)context["current_balls_remaining"];
        var newBallsInGame = new Dictionary<int, Ball>();
        foreach (var key in currentBallsRemainingDict.Keys)
            newBallsInGame[(int)key] = (Ball)currentBallsRemainingDict[key];
        BallsInGame = newBallsInGame;

        ApplyTurnAction(action);
    }

    private void ResetTurnState()
    {
        BallsScored.Clear();
        BallsOffTableList.Clear();
        FirstBallHit = null;
    }

    private Dictionary GenerateTurnContext()
    {
        var currentBallsRemaining = new Dictionary();
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

        var ballsScoredDict = new Dictionary();
        foreach (var kvp in BallsScored)
            ballsScoredDict[kvp.Key] = kvp.Value;

        return new Dictionary
        {
            ["balls_scored"] = ballsScoredDict,
            ["first_ball_touched"] = FirstBallHit,
            ["balls_off_table"] = new Array<Ball>(BallsOffTableList),
            ["target_ball"] = targetBall,
            ["current_balls_remaining"] = currentBallsRemaining,
        };
    }

    private void ApplyTurnAction(TurnRuler.Actions action)
    {
        switch (action)
        {
            case TurnRuler.Actions.CallNextTurn:
                PoolGame.CallNextTurn(new Dictionary());
                break;

            case TurnRuler.Actions.ExtendTurn:
                PoolGame.CallExtendCurrentTurn();
                break;

            case TurnRuler.Actions.CallCueBallReplacement:
                PoolGame.CallNextTurn(new Dictionary { ["ball_replacement"] = 0 });
                break;

            case TurnRuler.Actions.EndGameFatalFoul:
                PoolGame.CallMatchOver((string)PoolGame.TurnOwner.Name, new Dictionary { ["reason"] = "fatal_foul" });
                break;

            case TurnRuler.Actions.EndGamePlayerWin:
                PoolGame.CallMatchOver((string)PoolGame.TurnOwner.Name, new Dictionary { ["reason"] = "win" });
                Reset();
                break;
        }
    }
}
