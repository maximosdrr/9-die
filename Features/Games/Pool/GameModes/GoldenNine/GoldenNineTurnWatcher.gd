class_name GoldenNineTurnWatcher extends Node

var pool_game: PoolGame
var game_mode_handler: GameModeHandler
var ball_placement_manager: BallPlacementManager

var balls_scored: Dictionary[int, Ball] = {}
var balls_in_game: Dictionary[int, Ball] = {}
var cue_ball: Ball

var _first_ball_hit: Ball = null
var _balls_off_table_list: Array[Ball] = []

func setup(_pool_game: PoolGame):
	pool_game = _pool_game
	cue_ball = _pool_game.cue_ball
	game_mode_handler = _pool_game.game_mode_handler
	
	# Assumindo que o manager já existe no PoolGame
	ball_placement_manager = _pool_game.ball_placement_manager
	# Conectamos o sinal para saber quando o jogador terminou de posicionar
	ball_placement_manager.placement_finished.connect(_on_ball_placement_finished)
	
	pool_game.cue_ball.striked.connect(_on_strike)
	pool_game.score_monitor.body_entered.connect(_on_ball_touch_score_ground)
	
	if pool_game.off_table_monitor:
		pool_game.off_table_monitor.ball_fell_off.connect(_on_ball_fell_off)
	
	_reset_balls_in_game()

func _on_strike():
	balls_scored.clear()
	_balls_off_table_list.clear()
	_first_ball_hit = null
	
	if not cue_ball.ball_contacted.is_connected(_on_cue_ball_contact):
		cue_ball.ball_contacted.connect(_on_cue_ball_contact)
	
	await pool_game.balls_movement_monitor.balls_stopped
	
	if cue_ball.ball_contacted.is_connected(_on_cue_ball_contact):
		cue_ball.ball_contacted.disconnect(_on_cue_ball_contact)
	
	var current_balls_remaining = balls_in_game.duplicate()
	var target_ball_index = current_balls_remaining.keys().min()
	var target_ball = current_balls_remaining[target_ball_index]
	
	for scored_index in balls_scored:
		if current_balls_remaining.has(scored_index):
			current_balls_remaining.erase(scored_index)

	for ball in _balls_off_table_list:
		if current_balls_remaining.has(ball.index):
			current_balls_remaining.erase(ball.index)
	
	var context = TurnContext.new()
	context.balls_scored = balls_scored.duplicate()
	context.first_ball_touched = _first_ball_hit
	context.balls_off_table = _balls_off_table_list.duplicate()
	context.target_ball = target_ball
	
	var resolve_turn_action = game_mode_handler.resolve_turn(context)
	
	balls_in_game = current_balls_remaining
	
	_apply_turn_action(resolve_turn_action)

func _on_cue_ball_contact(other_ball: Ball):
	if _first_ball_hit == null:
		_first_ball_hit = other_ball

func _on_ball_fell_off(ball: Ball):
	if not _balls_off_table_list.has(ball):
		_balls_off_table_list.append(ball)

func _reset_balls_in_game():
	balls_in_game.clear()
	for ball in pool_game.balls:
		balls_in_game[ball.index] = ball

func _on_ball_touch_score_ground(body: Node3D):
	if body is Ball:
		balls_scored[body.index] = body

func _apply_turn_action(action: GameMode.TurnActions):
	match action:
		GameMode.TurnActions.CALL_NEXT_TURN:
			pool_game.call_next_turn()
			
		GameMode.TurnActions.EXTEND_TURN:
			_extend_turn()
			
		GameMode.TurnActions.CALL_FOUL_WITH_ACTION:
			var turn_context = {
				"ball_in_hand": true,
				"foul_reason": "scratch" 
			}
			pool_game.call_next_turn(turn_context)
			
		GameMode.TurnActions.END_GAME_FATAL_FOUL:
			_call_end_game_with_fatal_foul()
			
		GameMode.TurnActions.END_GAME_PLAYER_WIN:
			_call_end_game_with_winner()

func _start_ball_in_hand():
	if cue_ball.global_position.y < 0:
		cue_ball.global_position = Vector3(0, 1.0, 0)
		cue_ball.linear_velocity = Vector3.ZERO
		cue_ball.angular_velocity = Vector3.ZERO
	
	ball_placement_manager.start_placement(cue_ball)

func _on_ball_placement_finished():
	print("Posicionamento concluído. O Jogador " + pool_game.turn_owner.name + " pode realizar a tacada.")

# --- Helpers ---

func _extend_turn():
	print("Turn extended for: ", pool_game.turn_owner.name)

func _call_end_game_with_winner():
	print("Game Over! Winner: ", pool_game.turn_owner.name)

func _call_end_game_with_fatal_foul():
	print("Game Over! Fatal Foul by: ", pool_game.turn_owner.name)

class TurnContext:
	var balls_scored: Dictionary[int, Ball] = {}
	var first_ball_touched: Ball = null
	var balls_off_table: Array[Ball] = []
	var target_ball: Ball = null
