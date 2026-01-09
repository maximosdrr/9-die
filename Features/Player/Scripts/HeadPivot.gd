class_name HeadPivot
extends Node3D

@export var camera_mount: RemoteTransform3D

@export_group("Settings")
@export var mouse_sensitivity: float = 0.005
@export var min_pitch: float = -65.0
@export var max_pitch: float = 60.0

func _ready() -> void:
	pass

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	elif event.is_action_pressed("ui_cancel"):
		Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)

	if Input.get_mouse_mode() != Input.MOUSE_MODE_CAPTURED:
		return

	if event is InputEventMouseMotion:
		_handle_camera_rotation(event)

func _handle_camera_rotation(event: InputEventMouseMotion) -> void:
	if owner:
		owner.rotate_y(-event.relative.x * mouse_sensitivity)
	else:
		rotate_y(-event.relative.x * mouse_sensitivity)

	if camera_mount:
		camera_mount.rotate_x(-event.relative.y * mouse_sensitivity)
		
		camera_mount.rotation.x = clamp(
			camera_mount.rotation.x,
			deg_to_rad(min_pitch),
			deg_to_rad(max_pitch)
		)
