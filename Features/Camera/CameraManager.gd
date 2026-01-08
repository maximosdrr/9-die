class_name GlobalCamera extends Camera3D 

@export_category("References")
@export var player: Player
@export var table: Table

var remote_fps: RemoteTransform3D
var remote_aim: RemoteTransform3D
var remote_top: RemoteTransform3D

enum CamState { FPS, AIM, TOP }
var current_state: CamState = CamState.FPS

func _ready() -> void:
	remote_fps = player.remote_fps
	remote_aim = player.remote_aim
	remote_top = table.remote_top
	
	transition_to(CamState.FPS)

func transition_to(new_state: CamState) -> void:
	current_state = new_state
	
	_disable_remote(remote_fps)
	_disable_remote(remote_aim)
	_disable_remote(remote_top)
	
	match current_state:
		CamState.FPS:
			_enable_remote(remote_fps)
		CamState.AIM:
			_enable_remote(remote_aim)
		CamState.TOP:
			_enable_remote(remote_top)

func _disable_remote(remote: RemoteTransform3D) -> void:
	remote.update_position = false
	remote.update_rotation = false

func _enable_remote(remote: RemoteTransform3D) -> void:
	remote.use_global_coordinates = true 
	remote.update_position = true
	remote.update_rotation = true
