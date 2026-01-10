class_name PoolRoundManager extends Node3D

@export var state_machine: StateMachine
@export var ball_manager: Node

var players: Dictionary[int, Player] = {}

var turn_order: Array[int] = []
var current_turn_index: int = 0

func start_game(new_players: Dictionary[int, Player]) -> void:
	players = new_players
	
	turn_order = players.keys()
	turn_order.sort() 
	
	current_turn_index = 0
	
	var first_player_id = turn_order[current_turn_index]
	state_machine.change_state(State.Type.AIMING, { "player_id": first_player_id })

func get_current_player() -> Player:
	if players.is_empty() or turn_order.is_empty():
		return null
		
	var current_id = turn_order[current_turn_index]
	return players.get(current_id)

func advance_player_index() -> void:
	if turn_order.is_empty(): return
	current_turn_index = (current_turn_index + 1) % turn_order.size()

func are_balls_stopped() -> bool:
	return ball_manager.is_simulation_finished()
