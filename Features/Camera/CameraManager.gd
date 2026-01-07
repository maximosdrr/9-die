extends Node
class_name CameraManager

@export var remote_fps: RemoteTransform3D
@export var remote_aim: RemoteTransform3D
@export var remote_free: RemoteTransform3D

enum CamState { FPS, AIM, FREE }
var current_state: CamState = CamState.FPS

func _ready() -> void:
	transition_to(CamState.FPS)

func transition_to(new_state: CamState) -> void:
	print(new_state)
	current_state = new_state
	
	_disable_remote(remote_fps)
	_disable_remote(remote_aim)
	_disable_remote(remote_free)
	
	match current_state:
		CamState.FPS:
			_enable_remote(remote_fps)
		CamState.AIM:
			_enable_remote(remote_aim)
		CamState.FREE:
			_enable_remote(remote_free)

func _disable_remote(remote: RemoteTransform3D) -> void:
	remote.update_position = false
	remote.update_rotation = false

func _enable_remote(remote: RemoteTransform3D) -> void:
	remote.use_global_coordinates = true 
	remote.update_position = true
	remote.update_rotation = true
