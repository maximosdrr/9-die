using Godot;
using Godot.Collections;

[GlobalClass]
public partial class PoolGame : TableGame
{
    public PoolBallRespawn PoolBallRespawn;
    public OffTableMonitor OffTableMonitor;
    [Export] public PoolGameTable PoolTable;
    [Export] public PackedScene GameControllerScene;

    public BallsMovementMonitor BallsMovementMonitor;
    public BallPlacementManager BallPlacementManager;
    private GameModeHandler _gameModeHandler;

    public Ball CueBall = null;
    public Array<Ball> Balls = new();
    public Area3D ScoreMonitor;

    public override void _Ready()
    {
        PoolBallRespawn = GetNode<PoolBallRespawn>("Scripts/PoolBallRespawn");
        OffTableMonitor = GetNode<OffTableMonitor>("Scripts/OffTableMonitor");
        BallsMovementMonitor = GetNode<BallsMovementMonitor>("Scripts/BallsMovementMonitor");
        BallPlacementManager = GetNode<BallPlacementManager>("Scripts/BallPlacementManager");
        _gameModeHandler = GetNode<GameModeHandler>("GameModeHandler");

        ScoreMonitor = PoolTable.ScoreMonitor;

        OffTableMonitor.Setup(PoolTable.BallOffMonitor);
        GameModeHandler = _gameModeHandler;
        MatchOver += OnMatchIsOver;
        MatchStarted += OnMatchStarts;
    }

    public override async void SetupMatch(Array players, string firstTurnOwner)
    {
        PoolBallRespawn.StartGame();

        var (cueBall, balls) = await PoolBallRespawn.WaitTableReady();
        CueBall = cueBall;
        Balls = balls;

        _gameModeHandler.Setup(this);
        BallsMovementMonitor.Setup(this);

        EmitSignal(SignalName.MatchStarted, players, firstTurnOwner);
    }

    private void OnMatchStarts(Array playersIds, string firstTurnOwner)
    {
        foreach (var pIdVariant in playersIds)
        {
            var pId = (string)pIdVariant;
            var pNode = PlayerRegistry.Instance.GetPlayerById(pId);

            if (pNode != null)
                pNode.GameHandler.EquipGameController(GameControllerScene, this);
        }

        if (Player != null && Player.GameHandler.CurrentController != null)
            Player.GameHandler.CurrentController.ApplyControl(firstTurnOwner, new Dictionary());
    }

    private void OnMatchIsOver(string winner, Dictionary context)
    {
        Player.GameHandler.CurrentController.GiveControl();
        Player.GameHandler.UnequipCurrentController();
        Player.TakeControl();

        if (Multiplayer.IsServer())
            PoolBallRespawn.ClearTable();
    }
}
