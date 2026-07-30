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

    public override void Setup(TableGame tableGame)
    {
        PoolGame = (PoolGame)tableGame;
        CueBall = PoolGame.CueBall;

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
        SignalUtil.ConnectGuarded(PoolGame.ScoreMonitor, Area3D.SignalName.BodyEntered, new Callable(this, MethodName.OnBallTouchScoreGround));
        SignalUtil.ConnectGuarded(PoolGame.OffTableMonitor, OffTableMonitor.SignalName.BallFellOff, new Callable(this, MethodName.OnBallFellOff));
    }

    private void DisconnectSignals()
    {
        if (PoolGame == null)
            return;

        SignalUtil.DisconnectGuarded(PoolGame.CueBall, Ball.SignalName.Striked, new Callable(this, MethodName.OnStrike));
        SignalUtil.DisconnectGuarded(PoolGame, TableGame.SignalName.TurnChanged, new Callable(this, MethodName.OnTurnStart));
        SignalUtil.DisconnectGuarded(PoolGame.ScoreMonitor, Area3D.SignalName.BodyEntered, new Callable(this, MethodName.OnBallTouchScoreGround));
        SignalUtil.DisconnectGuarded(PoolGame.OffTableMonitor, OffTableMonitor.SignalName.BallFellOff, new Callable(this, MethodName.OnBallFellOff));
    }

    private void OnBallTouchScoreGround(Node3D body)
    {
        if (body is Ball ball)
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

        PoolGame.BallPlacementManager.StartPlacement(target, balls);
        await ToSignal(PoolGame.BallPlacementManager, BallPlacementManager.SignalName.PlacementFinished);
    }

    private async void OnStrike()
    {
        ResetTurnState();

        SignalUtil.ConnectGuarded(CueBall, Ball.SignalName.BallContacted, new Callable(this, MethodName.OnCueBallContact));
        await ToSignal(PoolGame.BallsMovementMonitor, BallsMovementMonitor.SignalName.BallsStopped);
        SignalUtil.DisconnectGuarded(CueBall, Ball.SignalName.BallContacted, new Callable(this, MethodName.OnCueBallContact));

        var context = GenerateTurnContext();
        var action = TurnRuler.Rule(context);

        BallsInGame = context.CurrentBallsRemaining;

        ApplyTurnAction(action);
    }

    private void ResetTurnState()
    {
        BallsScored.Clear();
        BallsOffTableList.Clear();
        FirstBallHit = null;
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
