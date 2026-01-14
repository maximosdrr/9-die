class_name PoolGame extends TableGame

@onready var pool_ball_respawn: PoolBallRespawn = $Scripts/PoolBallRespawn
@onready var pool_ball_girl: PoolBallGirl = $Scripts/PoolBallGirl
@onready var pool_turn_manager: PoolTurnManager = $Scripts/PoolGameManager
@onready var score_monitor: Area3D = $PoolTable/ScoreMonitor
@onready var balls_movement_monitor: BallsMovementMonitor = $Scripts/BallsMovementMonitor

var cue_ball: Ball = null
var balls: Array[Ball] = []

func _ready() -> void:
	assert(pool_ball_respawn.cue_ball != null)
	assert(pool_ball_respawn.balls.size() > 0)
	
	cue_ball = pool_ball_respawn.cue_ball
	balls = pool_ball_respawn.balls
	
	pool_ball_girl.setup(pool_ball_respawn.cue_ball)
	pool_turn_manager.setup(self)
	balls_movement_monitor.setup(self)
