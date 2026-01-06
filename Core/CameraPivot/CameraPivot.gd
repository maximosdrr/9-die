class_name CameraPivot extends Node3D

@export var camera: Camera3D
@export var rotation_x_enabled := true
@export var rotation_y_enabled := true
@export var yaw_sensitivity := 0.005

const BASE_YAW := deg_to_rad(180)
var rotation_target: Node3D = null

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	elif event.is_action_pressed("ui_cancel"):
		Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)

	if Input.get_mouse_mode() != Input.MOUSE_MODE_CAPTURED:
		return

	if event is not InputEventMouseMotion:
		return

	if rotation_x_enabled and camera:
		camera.rotate_x(-event.relative.y * 0.005)
		camera.rotation.x = clamp(
			camera.rotation.x,
			deg_to_rad(-65),
			deg_to_rad(60)
		)
	
	if not rotation_y_enabled:
		return
		
	if rotation_target != null:
		var yaw_delta = -event.relative.x * yaw_sensitivity
		_orbit_around_target(yaw_delta)
		return
	
	rotate_y(-event.relative.x * 0.005)

func _orbit_around_target(yaw_delta: float) -> void:
	var target_pos := rotation_target.global_transform.origin
	var offset := global_transform.origin - target_pos
	
	offset = offset.rotated(Vector3.UP, yaw_delta)

	global_transform.origin = target_pos + offset

	var flat_look := Vector3(target_pos.x, global_transform.origin.y, target_pos.z)
	look_at(flat_look, Vector3.UP)
