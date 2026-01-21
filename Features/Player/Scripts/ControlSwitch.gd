class_name ControlSwitch extends Node3D

@export var player: Player

func _unhandled_input(_event: InputEvent) -> void:
	if not is_multiplayer_authority():
		return

	if not Input.is_action_just_pressed("switch_control"):
		return
	
	var current_controller = pick_current_controller()
	
	if player.current_control_state == Player.ControllerStates.Player:
		_switch_to_game(current_controller)
	else:
		_switch_to_player(current_controller)

func _switch_to_player(game_controller: PlayerGameController):
	game_controller.give_control()
	player.take_control()
		
func _switch_to_game(game_controller: PlayerGameController):
	if not game_controller.can_take_control:
		push_error("Cannot take control of this game controller now!")
		return
	
	player.give_control()
	game_controller.take_control()

func pick_current_controller():
	if player.current_table_game is PoolGame:
		return player.pool_controller
	
	return player.pool_controller
