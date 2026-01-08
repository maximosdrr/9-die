class_name PlayerPovManager extends Node

@export var player: Player

var _is_aiming = false

func _unhandled_input(_event: InputEvent) -> void:
	# OLD VERIFICATION not table_balls_monitor.balls_are_stopped():
	if not Input.is_action_just_pressed("aim"):
		return

	if not _is_aiming and player.current_table != null:
		player.player_toggleable.disable()
		player.aim_toggleable.enable()
		player.global_camera.transition_to(GlobalCamera.CamState.AIM)
		_is_aiming = true
	
	elif _is_aiming:
		player.aim_toggleable.disable()
		player.player_toggleable.enable()
		player.global_camera.transition_to(GlobalCamera.CamState.FPS)
		_is_aiming = false
