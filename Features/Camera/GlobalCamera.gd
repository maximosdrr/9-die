class_name GlobalCamera extends Camera3D

@export_category("Properties")
@export var default_fov = 75.0

var current_remote: RemoteTransform3D = null

func set_global_camera_fov(new_fov: float):
	fov = new_fov

func reset_fov():
	fov = default_fov

func _enter_tree() -> void:
	Global.camera = self

func _exit_tree() -> void:
	if Global.camera == self:
		Global.camera = null

func transition_to(new_remote: RemoteTransform3D, _duration: float = 0.0) -> void:
	if current_remote and is_instance_valid(current_remote):
		_disable_remote(current_remote)
	
	current_remote = new_remote
	
	if current_remote:
		if current_remote.remote_path != get_path():
			current_remote.remote_path = get_path()
			
		_enable_remote(current_remote)
		

func _disable_remote(remote: RemoteTransform3D) -> void:
	remote.update_position = false
	remote.update_rotation = false
	remote.update_scale = false

func _enable_remote(remote: RemoteTransform3D) -> void:
	remote.use_global_coordinates = true 
	remote.update_position = true
	remote.update_rotation = true
	remote.update_scale = false
