class_name GoldenNineTurnResolver
extends TurnResolver

@export var turn_ruler: TurnRuler
@export var off_table_monitor: OffTableMonitor
@export var ball_placement_manager: BallPlacementManager

@onready var cue_ball_contact_listener: GoldenNineCueBallContactListener = $GoldenNineCueBallContactListener
@onready var score_listener: GoldenNineScoreListener = $GoldenNineScoreListener
@onready var ball_fell_off_listener: GoldenNineBallFellOffListener = $GoldenNineBallFellOffListener

var pool_game: PoolGame
var cue_ball: Ball

var balls_scored: Dictionary[int, Ball] = {}
var balls_in_game: Dictionary[int, Ball] = {}
var balls_off_table_list: Array[Ball] = []
var first_ball_hit: Ball = null

func setup(_pool_game: TableGame) -> void:
	pool_game = _pool_game
	cue_ball = _pool_game.cue_ball
	
	score_listener.setup(self)
	cue_ball_contact_listener.setup(self)
	ball_fell_off_listener.setup(self)
	
	balls_in_game.clear()
	
	for ball in pool_game.balls:
		balls_in_game[ball.index] = ball
		
	_connect_signals()

func reset() -> void:
	_disconnect_signals()
	
	balls_scored.clear()
	balls_in_game.clear()
	balls_off_table_list.clear()
	first_ball_hit = null
	
	cue_ball = null
	pool_game = null

func _connect_signals() -> void:
	if not pool_game:
		return
		
	if not pool_game.cue_ball.striked.is_connected(_on_strike):
		pool_game.cue_ball.striked.connect(_on_strike)
	
	if not pool_game.turn_changed.is_connected(_on_turn_start):
		pool_game.turn_changed.connect(_on_turn_start)

func _disconnect_signals() -> void:
	if not pool_game:
		return

	if pool_game.cue_ball.striked.is_connected(_on_strike):
		pool_game.cue_ball.striked.disconnect(_on_strike)
	
	if pool_game.turn_changed.is_connected(_on_turn_start):
		pool_game.turn_changed.disconnect(_on_turn_start)

func _on_turn_start(owner_id: String, context: Dictionary) -> void:
	if multiplayer.get_unique_id() != int(owner_id):
		return
	
	if context.has("ball_replacement"):
		_handle_ball_replacement(context)

func _handle_ball_replacement(context: Dictionary) -> void:
	var ball_index: int = context.get("ball_replacement")
	var target: Ball = cue_ball if ball_index == 0 else balls_in_game[ball_index]
	var balls: Array[Ball] = context.get("current_balls_remaining") if \
		context.has("current_balls_remaining") else balls_in_game.values()
	
	ball_placement_manager.start_placement(target, balls)
	await ball_placement_manager.placement_finished

func _on_strike() -> void:
	_reset_turn_state()
	
	cue_ball_contact_listener.start_listening_collisions()
	await pool_game.balls_movement_monitor.balls_stopped
	cue_ball_contact_listener.stop_listening_collisions()
	
	var context: Dictionary = _generate_turn_context()
	var action: TurnRuler.Actions = turn_ruler.rule(context)
	
	balls_in_game = context.get("current_balls_remaining")
	
	_apply_turn_action(action)

func _reset_turn_state() -> void:
	balls_scored.clear()
	balls_off_table_list.clear()
	first_ball_hit = null

func _generate_turn_context() -> Dictionary:
	var current_balls_remaining: Dictionary = balls_in_game.duplicate()
	
	if current_balls_remaining.is_empty():
		push_error("ERRO CRÍTICO: Nenhuma bola registrada no TurnResolver!")

	var target_ball_index: int = current_balls_remaining.keys().min()
	var target_ball: Ball = current_balls_remaining[target_ball_index]
	
	for scored_index in balls_scored:
		current_balls_remaining.erase(scored_index)

	for ball in balls_off_table_list:
		current_balls_remaining.erase(ball.index)
	
	return {
		"balls_scored": balls_scored.duplicate(),
		"first_ball_touched": first_ball_hit,
		"balls_off_table": balls_off_table_list.duplicate(),
		"target_ball": target_ball,
		"current_balls_remaining": current_balls_remaining
	}

func _apply_turn_action(action: TurnRuler.Actions) -> void:
	match action:
		TurnRuler.Actions.CALL_NEXT_TURN:
			pool_game.call_next_turn({})
			
		TurnRuler.Actions.EXTEND_TURN:
			pool_game.call_extend_current_turn()
			
		TurnRuler.Actions.CALL_CUE_BALL_REPLACEMENT:
			pool_game.call_next_turn({ "ball_replacement": 0 })
			
		TurnRuler.Actions.END_GAME_FATAL_FOUL:
			pool_game.call_match_over(pool_game.turn_owner.name, { "reason": "fatal_foul" })
			
		TurnRuler.Actions.END_GAME_PLAYER_WIN:
			pool_game.call_match_over(pool_game.turn_owner.name, { "reason": "win" })
			reset()
