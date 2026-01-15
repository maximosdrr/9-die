class_name PoolGame extends TableGame

@onready var pool_ball_respawn: PoolBallRespawn = $Scripts/PoolBallRespawn
@onready var off_table_monitor: OffTableMonitor = $Scripts/OffTableMonitor
@onready var score_monitor: Area3D = $PoolTable/ScoreMonitor
@onready var balls_movement_monitor: BallsMovementMonitor = $Scripts/BallsMovementMonitor
@onready var ball_placement_manager: BallPlacementManager = $Scripts/BallPlacementManager
@onready var _game_mode_handler: GameModeHandler = $GameModeHandler

var cue_ball: Ball = null
var balls: Array[Ball] = []

func _ready() -> void:
	assert(pool_ball_respawn.cue_ball != null)
	assert(pool_ball_respawn.balls.size() > 0)
	
	cue_ball = pool_ball_respawn.cue_ball
	balls = pool_ball_respawn.balls
	
	balls_movement_monitor.setup(self)
	_game_mode_handler.setup(self)
	game_mode_handler = _game_mode_handler
