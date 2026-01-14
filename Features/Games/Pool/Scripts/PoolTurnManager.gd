class_name PoolTurnManager extends Node

signal turn_changed(player_id: String)

var pool_game: PoolGame
var game_mode_handler: GameModeHandler

# Estado global das bolas
var balls_scored: Dictionary[int, Ball] = {}
var balls_in_game: Dictionary[int, Ball] = {}
var cue_ball: Ball

# Estado temporário do turno
var _first_ball_hit: Ball = null

func setup(_pool_game: PoolGame):
	pool_game = _pool_game
	cue_ball = _pool_game.cue_ball
	game_mode_handler = _pool_game.game_mode_handler
	
	pool_game.cue_ball.striked.connect(_on_strike)
	pool_game.score_monitor.body_entered.connect(_on_ball_touch_score_ground)
	
	_reset_balls_in_game()

func _on_strike():
	# 1. Resetar estados do turno
	balls_scored.clear()
	_first_ball_hit = null 
	
	if not cue_ball.ball_contacted.is_connected(_on_cue_ball_contact):
		cue_ball.ball_contacted.connect(_on_cue_ball_contact)
	
	# 2. Esperar tudo parar
	await pool_game.balls_movement_monitor.balls_stopped
	
	if cue_ball.ball_contacted.is_connected(_on_cue_ball_contact):
		cue_ball.ball_contacted.disconnect(_on_cue_ball_contact)
	
	# 3. CORREÇÃO PRINCIPAL AQUI:
	# Criamos uma simulação das bolas que sobraram para enviar ao Handler
	var current_balls_remaining = balls_in_game.duplicate()
	
	# Removemos dessa lista temporária as bolas que caíram neste turno
	for scored_index in balls_scored:
		if current_balls_remaining.has(scored_index):
			current_balls_remaining.erase(scored_index)
	
	# 4. Criar Contexto com a lista JÁ FILTRADA
	var context = TurnContext.new()
	context.balls_scored = balls_scored.duplicate()
	context.balls_in_game = current_balls_remaining # Agora envia só as que sobraram
	context.first_ball_touched = _first_ball_hit
	
	# 5. Resolver regras
	var resolve_turn_action = game_mode_handler.resolve_turn(context)
	
	# 6. Atualizar o estado real da classe (persistir a remoção)
	balls_in_game = current_balls_remaining
	
	_apply_turn_action(resolve_turn_action)

# --- Helpers ---

func _on_cue_ball_contact(other_ball: Ball):
	if _first_ball_hit == null:
		_first_ball_hit = other_ball

func _reset_balls_in_game():
	balls_in_game.clear()
	# Importante: Certifique-se que pool_game.balls contém TODAS as bolas numeradas (exceto a branca se preferir)
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
			#_handle_foul_penalties()
			pool_game.call_next_turn()
			
		GameMode.TurnActions.END_GAME_FATAL_FOUL:
			_call_end_game_with_fatal_foul()
			
		GameMode.TurnActions.END_GAME_PLAYER_WIN:
			_call_end_game_with_winner()
			
func _extend_turn():
	print("Turn extended for: ", pool_game.turn_owner.name)

func _call_end_game_with_winner():
	print("Game Over! Winner: ", pool_game.turn_owner.name)

func _call_end_game_with_fatal_foul():
	print("Game Over! Fatal Foul by: ", pool_game.turn_owner.name)

# Inner Class
class TurnContext:
	var balls_scored: Dictionary[int, Ball] = {}
	var balls_in_game: Dictionary[int, Ball] = {}
	var first_ball_touched: Ball = null
	var balls_off_table = []
