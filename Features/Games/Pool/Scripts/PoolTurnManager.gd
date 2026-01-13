class_name PoolTurnManager extends Node

signal turn_changed(player_id: String)

var pool_game: PoolGame
var game_mode_handler: GameModeHandler

var balls_scored: Dictionary[int, Ball] = {}
var balls_in_game: Dictionary[int, Ball] = {}

func setup(_pool_game: PoolGame):
	pool_game = _pool_game
	game_mode_handler = _pool_game.game_mode_handler
	pool_game.cue_ball.striked.connect(_on_strike)
	pool_game.score_monitor.body_entered.connect(_on_ball_touch_score_ground)
	_update_balls_in_game({})


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
			pool_game.call_next_turn()
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

func _extend_turn():
	print("Turn extended for: ", pool_game.turn_owner.name)

func _call_end_game_with_winner():
	print("game is over! Winner: ", pool_game.turn_owner.name)

func _call_end_game_with_fatal_foul():
	print("game is over! Winner: ", pool_game.turn_owner.name)
