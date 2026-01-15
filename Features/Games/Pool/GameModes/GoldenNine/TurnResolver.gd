class_name GoldenNineTurnResolver extends TurnResolver

@export var turn_ruler: TurnRuler
@export var off_table_monitor: OffTableMonitor
@export var ball_placement_manager: BallPlacementManager

#Listeners. They can modify GoldenNineTurnResolve props! Be careful
@onready var cue_ball_contact_listener: GoldenNineCueBallContactListener = $GoldenNineCueBallContactListener
@onready var score_listener: GoldenNineScoreListener = $GoldenNineScoreListener
@onready var ball_fell_off_listener: GoldenNineBallFellOffListener = $GoldenNineBallFellOffListener

var pool_game: PoolGame
var balls_scored: Dictionary[int, Ball] = {}
var balls_in_game: Dictionary[int, Ball] = {}
var cue_ball: Ball

var first_ball_hit: Ball = null
var balls_off_table_list: Array[Ball] = []

func setup(_pool_game: TableGame):
	pool_game = _pool_game
	cue_ball = _pool_game.cue_ball
	
	pool_game.cue_ball.striked.connect(_on_strike)
	pool_game.turn_changed.connect(_on_turn_start)
	
	score_listener.setup(self)
	cue_ball_contact_listener.setup(self)
	ball_fell_off_listener.setup(self)
	
	for ball in pool_game.balls:
		balls_in_game[ball.index] = ball

func _on_turn_start(owner_id: String, context: Dictionary):
	if multiplayer.get_unique_id() != int(owner_id):
		return
	
	if context.has("ball_replacement"):
		_call_ball_replacement(context)

func _call_ball_replacement(context: Dictionary):
	var ball_index = context.get("ball_replacement")
	var target = cue_ball if ball_index == 0 else balls_in_game[ball_index]
	
	ball_placement_manager.start_placement(target)
	
	await ball_placement_manager.placement_finished
	print("Posicionamento finalizado e autoridade devolvida ao servidor.")

func _on_strike():
	balls_scored.clear()
	balls_off_table_list.clear()
	first_ball_hit = null
	
	cue_ball_contact_listener.start_listening_collisions()
	await pool_game.balls_movement_monitor.balls_stopped
	cue_ball_contact_listener.stop_listening_collisions()
	
	var context = generate_turn_context()
	
	var resolve_turn_action = turn_ruler.rule(context)
	balls_in_game = context.get("current_balls_remaining")
	
	_apply_turn_action(resolve_turn_action)

func generate_turn_context():
	var context = {}
	
	var current_balls_remaining = balls_in_game.duplicate()
	var target_ball_index = current_balls_remaining.keys().min()
	var target_ball = current_balls_remaining[target_ball_index]
	
	for scored_index in balls_scored:
		if current_balls_remaining.has(scored_index):
			current_balls_remaining.erase(scored_index)

	for ball in balls_off_table_list:
		if current_balls_remaining.has(ball.index):
			current_balls_remaining.erase(ball.index)
	
	context["balls_scored"] = balls_scored.duplicate()
	context["first_ball_touched"] = first_ball_hit
	context["balls_off_table"] = balls_off_table_list.duplicate()
	context["target_ball"] = target_ball
	context["current_balls_remaining"] = current_balls_remaining
	
	return context

func _apply_turn_action(action: TurnRuler.Actions):
	match action:
		TurnRuler.Actions.CALL_NEXT_TURN:
			pool_game.call_next_turn({})
			
		TurnRuler.Actions.EXTEND_TURN:
			pool_game.call_extend_current_turn()
			
		TurnRuler.Actions.CALL_CUE_BALL_REPLACEMENT:
			pool_game.call_next_turn({
				"ball_replacement": 0
			})
			
		TurnRuler.Actions.END_GAME_FATAL_FOUL:
			print("Game over! Fatal Foul")
			
		TurnRuler.Actions.END_GAME_PLAYER_WIN:
			print("Game over! Win!")
