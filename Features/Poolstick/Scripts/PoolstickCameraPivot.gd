class_name PollstickCameraPivot extends Node3D 

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
		_orbit_around_target(yaw_delta)

func _orbit_around_target(yaw_delta: float) -> void:
	if rotation_target == null:
		return
	var target_pos := rotation_target.global_transform.origin
	var offset := global_transform.origin - target_pos
	offset = offset.rotated(Vector3.UP, yaw_delta)

	global_transform.origin = target_pos + offset

	# trava a altura pra não inclinar e "abaixar" ao orbitar
	var flat_look := Vector3(target_pos.x, global_transform.origin.y, target_pos.z)
	look_at(flat_look, Vector3.UP)
