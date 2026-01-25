class_name PoolGame extends Table

@onready var pool_ball_respawn: PoolBallRespawn = $Scripts/PoolBallRespawn
@onready var off_table_monitor: OffTableMonitor = $Scripts/OffTableMonitor
@onready var balls_movement_monitor: BallsMovementMonitor = $Scripts/BallsMovementMonitor
@onready var ball_placement_manager: BallPlacementManager = $Scripts/BallPlacementManager

@onready var _match_manager: MatchManager = $MatchManager
@onready var _game_controller: PoolController = $PoolController

@export var pool_table: PoolGameTable

var cue_ball: Ball = null
var balls: Array[Ball] = []
var score_monitor: Area3D

func _ready() -> void:
	add_to_group(Groups.TABLE)
	match_manager = _match_manager
	game_controller = _game_controller
	
	score_monitor = pool_table.score_monitor
	off_table_monitor.setup(pool_table.ball_off_monitor)
	
	name = 'Pool_%s' % [get_instance_id()]


func start_match(_players: Array[String]):
	players = _players
	match_manager.start_match(players, {})
	pool_ball_respawn.start_game()
	
