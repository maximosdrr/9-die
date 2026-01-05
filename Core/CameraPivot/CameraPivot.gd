class_name CameraPivot extends Node3D

@export var camera: Camera3D
@export var rotation_x_enabled := true
@export var rotation_y_enabled := true

const BASE_YAW := deg_to_rad(180)

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

	if rotation_y_enabled:
		rotate_y(-event.relative.x * 0.005)
