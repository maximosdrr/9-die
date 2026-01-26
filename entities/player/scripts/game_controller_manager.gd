class_name GameControllerManager extends Node

@export var player: Player

var current_game_mode = GameMode.PLAYER

enum GameMode {
	PLAYER,
	GAME_CONTROLLER
}

func _apply_control():
	if not is_multiplayer_authority():
		return

	if player.table == null:
		push_error("There's no table near to the player!")
		return
	
	if player.table.match_manager.turn_owner != str(multiplayer.get_unique_id()):
		print("It's not your turn yet!")
		return
	
	if current_game_mode == GameMode.PLAYER:
		_take_control()
	else:
		_drop_control()
	
func _take_control():
	player.head_pivot.set_process_unhandled_input(false)
	player.table.game_controller.take_control()
	current_game_mode = GameMode.GAME_CONTROLLER
	
func _drop_control():
	player.head_pivot.set_process_unhandled_input(true)
	player.table.game_controller.drop_control()
	Global.camera.transition_to(player.remote_fps)
	current_game_mode = GameMode.PLAYER


func _unhandled_input(_event: InputEvent) -> void:
	if Input.is_action_just_pressed("switch_control"):
		_apply_control()
