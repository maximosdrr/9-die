class_name LevelCameraController extends Node

@export var reference_ball: Ball
@export var player: Player
@export var poolstick_respawn_controller: PoolstickRespawnController

var is_aiming = false
var current_poolstick: Poolstick = null

func _unhandled_input(event: InputEvent) -> void:
	if not event.is_action_pressed("aim"):
		return
	
	if not is_aiming:
		poolstick_respawn_controller.respawn_at_marker(reference_ball.poolstick_respawn_marker)
		current_poolstick = poolstick_respawn_controller.current_poolstick
		
		player.set_player_camera_current(false)
		current_poolstick.set_poolstick_camera_current(true)
		current_poolstick.add_camera_target(reference_ball)
		is_aiming = true
	else:
		player.set_player_camera_current(true)
		current_poolstick.set_poolstick_camera_current(false)
		current_poolstick.remove_camera_target()
		is_aiming = false
