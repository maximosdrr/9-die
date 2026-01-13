class_name PoolGame extends TableGame

@onready var pool_ball_respawn: PoolBallRespawn = $Scripts/PoolBallRespawn
@onready var pool_ball_girl: PoolBallGirl = $Scripts/PoolBallGirl
@onready var pool_turn_manager: PoolTurnManager = $Scripts/PoolGameManager

var cue_ball: Ball = null

func _ready() -> void:
	assert(pool_ball_respawn.cue_ball != null)
	cue_ball = pool_ball_respawn.cue_ball
	pool_ball_girl.setup(pool_ball_respawn.cue_ball)
	pool_turn_manager.setup(self)
