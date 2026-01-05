extends Node3D
class_name CameraPivot

@export var camera: Camera3D
@export var rotation_x_enabled := true
@export var rotation_y_enabled := true

@export var rotation_target: Node3D

@export var yaw_sensitivity := 0.005
@export var pitch_sensitivity := 0.005

@export var min_pitch_deg := -65.0
@export var max_pitch_deg := 60.0

func set_rotation_target(target: Node3D) -> void:
	rotation_target = target

func clear_rotation_target() -> void:
	rotation_target = null

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
		camera.rotate_x(-event.relative.y * pitch_sensitivity)
		camera.rotation.x = clamp(
			camera.rotation.x,
			deg_to_rad(min_pitch_deg),
			deg_to_rad(max_pitch_deg)
		)

	if rotation_y_enabled:
		var yaw_delta = -event.relative.x * yaw_sensitivity

		if rotation_target:
			_orbit_around_target(rotation_target, yaw_delta)
		else:
			rotate_y(yaw_delta)
			
func _orbit_around_target(target: Node3D, yaw_delta: float) -> void:
	var target_pos := target.global_transform.origin

	var offset := global_transform.origin - target_pos
	offset = offset.rotated(Vector3.UP, yaw_delta)

	global_transform.origin = target_pos + offset

	look_at(target_pos, Vector3.UP)
