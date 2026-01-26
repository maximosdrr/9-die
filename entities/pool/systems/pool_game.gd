class_name PoolGame extends Table

@onready var off_table_monitor: OffTableMonitor = $Scripts/OffTableMonitor
@onready var balls_movement_monitor: BallsMovementMonitor = $Scripts/BallsMovementMonitor
@onready var ball_placement_manager: BallPlacementManager = $Scripts/BallPlacementManager
@onready var balls_holder: BallHolder = $BallsHolder

@onready var _match_manager: MatchManager = $MatchManager
@onready var _game_controller: PoolController = $PoolController

@export var pool_table: PoolGameTable

var cue_ball: Ball = null
var balls: Array[Ball] = []
var score_monitor: Area3D
var turn_data: PoolTurnData = PoolTurnData.new()
var golden_nine_mode: GoldenNineMode = GoldenNineMode.new()

func _ready() -> void:
	add_to_group(Groups.TABLE)
	match_manager = _match_manager
	game_controller = _game_controller
	
	score_monitor = pool_table.score_monitor
	off_table_monitor.setup(pool_table.ball_off_monitor)
	
	#Connect signals
	match_manager.match_started.connect(_on_match_starts)
	match_manager.turn_changed.connect(_on_turn_changes)
	game_controller.cue.start_track.connect(_track_turn)
	off_table_monitor.ball_fell_off.connect(_on_ball_dropped_off)
	
	name = 'Pool_%s' % [get_instance_id()]

func start_match(_players: Array[String]):
	match_manager.start_match(_players, {})
	balls_holder.spawn_balls()

func _on_match_starts():
	await balls_holder.table_ready
	cue_ball = balls_holder.get_cue_ball()
	balls = balls_holder.get_normal_balls()
	game_controller.setup()
	balls_movement_monitor.setup(self)
	
func _on_turn_changes(new_owner_id: String):
	game_controller.set_multiplayer_authority(int(new_owner_id))
	
	if new_owner_id != str(multiplayer.get_unique_id()):
		return
	
	if match_manager.turn_metadata.has("replace_ball"):
		_game_controller.cue.lock_cue()
		ball_placement_manager.start_placement(cue_ball, balls)
		await ball_placement_manager.placement_finished
		
	game_controller.move_to_cue_ball()
	await get_tree().create_timer(1.5).timeout
	_game_controller.cue.release_cue()

func _track_turn():
	turn_data = PoolTurnData.new()
	pool_table.score_monitor.body_entered.connect(_on_ball_pocketed)
	await balls_movement_monitor.balls_stopped
	pool_table.score_monitor.body_entered.disconnect(_on_ball_pocketed)
	var turn_action = golden_nine_mode.resolve_turn(turn_data)
	_resolve_turn(turn_action)

func _on_ball_pocketed(body: Node3D):
	if not body is Ball: return
	if turn_data.pocketed_balls.has(body.index): return
	turn_data.pocketed_balls.set(body.index, body)

func _on_ball_dropped_off(ball: Ball):
	if turn_data.balls_dropped_off.has(ball.index): return
	turn_data.balls_dropped_off.set(ball.index, ball)

func _resolve_turn(command: String):
	if command == PoolTurnCommands.CALL_NEXT_TURN:
		if multiplayer.is_server():
			match_manager.call_next_turn({})
	if command == PoolTurnCommands.CALL_EXTEND_TURN:
		game_controller.cue.release_cue()
		game_controller.move_to_cue_ball()
	if command == PoolTurnCommands.CALL_BALL_REPLACEMENT:
		match_manager.call_next_turn({
			"replace_ball": true
		})
	if command == PoolTurnCommands.CALL_MATCH_OVER_LOSER:
		#TODO Loser do something
		if multiplayer.is_server():
			balls_holder.clear_table()
	if command == PoolTurnCommands.CALL_MATCH_OVER_WINNER:
		#TODO Winner do something
		if multiplayer.is_server():
			balls_holder.clear_table()
	
