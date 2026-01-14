class_name PoolGame extends TableGame

@onready var pool_ball_respawn: PoolBallRespawn = $Scripts/PoolBallRespawn
@onready var off_table_monitor: OffTableMonitor = $Scripts/OffTableMonitor
@onready var golden_nine_turn_watcher: GoldenNineTurnWatcher = $Scripts/GoldenNineTurnWatcher
@onready var score_monitor: Area3D = $PoolTable/ScoreMonitor
@onready var balls_movement_monitor: BallsMovementMonitor = $Scripts/BallsMovementMonitor
@onready var ball_placement_manager: BallPlacementManager = $Scripts/BallPlacementManager

var cue_ball: Ball = null
var balls: Array[Ball] = []

func _ready() -> void:
	assert(pool_ball_respawn.cue_ball != null)
	assert(pool_ball_respawn.balls.size() > 0)
	
	cue_ball = pool_ball_respawn.cue_ball
	balls = pool_ball_respawn.balls
	
	golden_nine_turn_watcher.setup(self)
	balls_movement_monitor.setup(self)

func _handle_turn_context(data: Dictionary):
	if data.get("ball_in_hand", false) == true:
		print("Recebido contexto de Ball in Hand via Rede!")
		ball_placement_manager.start_placement(cue_ball)
