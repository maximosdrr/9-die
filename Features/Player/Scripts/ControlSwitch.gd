class_name ControlSwitch extends Node

@export var player: Player
@export var game_context_slot: Node3D

enum ControllerStates { Player, Game }

var current_control_state = ControllerStates.Player

func _unhandled_input(_event: InputEvent) -> void:
	if not is_multiplayer_authority():
		return

	if not Input.is_action_just_pressed("switch_control"):
		return
		
	var game_context_children = game_context_slot.get_children()
	
	if game_context_children.size() == 0:
		push_error("No children in player game controller context yet!")
		return
	
	var current_game_controller = game_context_children[0]
	
	if current_game_controller == null:
		return
	
	assert(current_game_controller is PlayerGameController)
	
	if current_control_state == ControllerStates.Player:
		_switch_to_game(current_game_controller)
	else:
		_switch_to_player(current_game_controller)

func _switch_to_player(game_controller: PlayerGameController):
	game_controller.give_control()
	player.take_control()
	current_control_state = ControllerStates.Player
		
func _switch_to_game(game_controller: PlayerGameController):
	#TODO verify if can take control before give control
	#Otherwise if you cannot take control at that moment you will be in a limbo
	player.give_control()
	game_controller.take_control()
	current_control_state = ControllerStates.Game
