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

	public Dictionary<string, Array> BallsPocketedByPlayer = new();
	public int CurrentTargetBallIndex = 0;
	public GlobalCamera Camera;

	[Signal]
	public delegate void HudStateUpdatedEventHandler();

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
		PlayerRemovedFromMatch += OnPlayerRemovedFromMatch;
		PlayerReclaimed += OnPlayerReclaimed;
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

	public override async void SetupMatch(Array players, string firstTurnOwner)
	{
		BallsPocketedByPlayer.Clear();
		CurrentTargetBallIndex = 0;

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
				pNode.GameHandler.EquipGameController(GameControllerScene, this, Camera);
		}

		if (Player != null && Player.GameHandler.CurrentController != null)
			Player.GameHandler.CurrentController.ApplyControl(firstTurnOwner, new Dictionary());
	}

	private void OnMatchIsOver(string winner, Dictionary context)
	{
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

	private void OnPlayerReclaimed(string oldPlayerId, string newPlayerId, Array turnOrder)
	{
		var newPlayer = PlayerRegistry.Instance.GetPlayerById(newPlayerId);
		if (newPlayer == null)
			return;

		newPlayer.GameHandler.EquipGameController(GameControllerScene, this, Camera);

		// The old player's node is already gone by now, so TurnOwner (if it was
		// theirs) is a stale reference — this just checks whose turn it names,
		// same pattern RemovePlayerFromMatch already relies on being safe here.
		var wasTurnOwner = TurnOwner != null && (string)TurnOwner.Name == oldPlayerId;
		if (!wasTurnOwner)
			return;

		TurnOwner = newPlayer;
		newPlayer.GameHandler.CurrentController?.ApplyControl(newPlayerId, new Dictionary());
	}
}
