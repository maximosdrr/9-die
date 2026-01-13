class_name PoolTurnManager extends Node

signal turn_changed(player_id: String)

var pool_game: PoolGame
var turn_order: Array = []
var match_started: bool = false
var game_mode_handler: GameModeHandler

var balls_scored: Dictionary[int, Ball] = {}
var balls_in_game: Dictionary[int, Ball] = {}

func setup(_pool_game: PoolGame):
	pool_game = _pool_game
	game_mode_handler = _pool_game.game_mode_handler
	pool_game.table.match_started.connect(_on_match_started)
	pool_game.cue_ball.striked.connect(_on_strike)
	pool_game.score_monitor.body_entered.connect(_on_ball_touch_score_ground)
	_update_balls_in_game({})

func _on_match_started(players: Array[String]):
	if match_started: return

	match_started = true
	turn_order = players
	
	var first_turn_owner_id = turn_order[0]
	var player = PlayerRegistry.get_player_by_id(first_turn_owner_id)
	
	pool_game.turn_owner = player
	turn_changed.emit(first_turn_owner_id)

func _on_strike():
	balls_scored.clear()
	
	await pool_game.balls_movement_monitor.balls_stopped
	
	var context = {}
	
	context.set("balls_scored", balls_scored)
	context.set("balls_in_game", balls_in_game)
	context.set("balls_off_table", [])
	#TODO context.set("first_ball_touched", ?)
	var resolve_turn_action = game_mode_handler.resolve_turn(
		context
	)
	
	_update_balls_in_game(balls_scored)
	
	match resolve_turn_action:
		GameMode.TurnActions.CALL_NEXT_TURN:
			_call_next_turn()
		GameMode.TurnActions.EXTEND_TURN:
			_extend_turn()
		GameMode.TurnActions.END_GAME_FATAL_FOUL:
			_call_end_game_with_fatal_foul()
		GameMode.TurnActions.END_GAME_PLAYER_WIN:
			_call_end_game_with_winner()

func _update_balls_in_game(subtract: Dictionary[int, Ball]):
	for ball in pool_game.balls:
		if not subtract.has(ball.index):
			balls_in_game.set(ball.index, ball)
	

func _on_ball_touch_score_ground(body: Node3D):
	if body is Ball:
		balls_scored.set(body.index, body)

func _call_next_turn():
	var current_id = pool_game.turn_owner.name
	var current_index = turn_order.find(current_id)
	
	if current_index == -1:
		return
		
	var next_index = (current_index + 1) % turn_order.size()
	var next_player_id = turn_order[next_index]
	
	var next_player = PlayerRegistry.get_player_by_id(next_player_id)
	pool_game.turn_owner = next_player
	
	turn_changed.emit(next_player_id)
	print("Turn passed to: ", next_player_id)

func _extend_turn():
	print("Turn extended for: ", pool_game.turn_owner.id)

func _call_end_game_with_winner():
	print("game is over! Winner: ", pool_game.turn_owner.name)

func _call_end_game_with_fatal_foul():
	print("game is over! Winner: ", pool_game.turn_owner.name)
