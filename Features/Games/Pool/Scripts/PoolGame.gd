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
	score_monitor = pool_table.score_monitor
	
	off_table_monitor.setup(pool_table.ball_off_monitor)
	game_mode_handler = _game_mode_handler
	match_over.connect(_on_match_is_over)
	match_started.connect(_on_match_starts)
	

func setup_match(players: Array, first_turn_owner: String) -> void:
	pool_ball_respawn.start_game()

	var payload := await pool_ball_respawn.wait_table_ready()
	cue_ball = payload[0]
	balls = payload[1]

	_game_mode_handler.setup(self)
	balls_movement_monitor.setup(self)

	match_started.emit(players, str(first_turn_owner))

func _on_match_starts(players_ids: Array, first_turn_owner: String):
	for p_id in players_ids:
		var p_node = PlayerRegistry.get_player_by_id(p_id)
		
		if p_node:
			p_node.game_handler.equip_game_controller(
				table.game_controller_scene,
				self
			)
	
	if player and player.game_handler.current_controller:
		player.game_handler.current_controller.apply_control(first_turn_owner, {})

func _on_match_is_over(_winner: String, _context: Dictionary):
	player.game_handler.current_controller.give_control()
	player.game_handler.unequip_current_controller()
	player.take_control()
	pool_ball_respawn.clear_table()
