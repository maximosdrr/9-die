class_name ControlSwitch extends Node

@export var player: Player
@export var game_context_slot: Node3D

enum ControllerStates { Player, Game }

var current_control_state = ControllerStates.Player

func _unhandled_input(_event: InputEvent) -> void:
	if not Input.is_action_just_pressed("switch_control"):
		return
	
	var current_game_controller = game_context_slot.get_children()[0]
	
	if current_game_controller == null:
		return
	
	assert(current_game_controller is GameController)
	
	if current_control_state == ControllerStates.Player:
		switch_to_game(current_game_controller)
	else:
		switch_to_player(current_game_controller)

func switch_to_player(game_controller: GameController):
	game_controller.give_control()
	player.take_control()
	current_control_state = ControllerStates.Player
		
func switch_to_game(game_controller: GameController):
	player.give_control()
	game_controller.take_control()
	current_control_state = ControllerStates.Game
