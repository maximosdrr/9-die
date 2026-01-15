class_name PoolGame extends TableGame

@onready var pool_ball_respawn: PoolBallRespawn = $Scripts/PoolBallRespawn
@onready var off_table_monitor: OffTableMonitor = $Scripts/OffTableMonitor
@export var pool_table: PoolGameTable

@onready var balls_movement_monitor: BallsMovementMonitor = $Scripts/BallsMovementMonitor
@onready var ball_placement_manager: BallPlacementManager = $Scripts/BallPlacementManager
@onready var _game_mode_handler: GameModeHandler = $GameModeHandler

var cue_ball: Ball = null
var balls: Array[Ball] = []
var score_monitor: Area3D

func _ready() -> void:
	assert(pool_ball_respawn.cue_ball != null)
	assert(pool_ball_respawn.balls.size() > 0)
	
	cue_ball = pool_ball_respawn.cue_ball
	balls = pool_ball_respawn.balls
	score_monitor = pool_table.score_monitor
	
	balls_movement_monitor.setup(self)
	_game_mode_handler.setup(self)
	off_table_monitor.setup(pool_table.ball_off_monitor)
	game_mode_handler = _game_mode_handler
