class_name HeadPivot
extends Node3D

@export var camera_mount: RemoteTransform3D

var player_body: CharacterBody3D

@export_group("Settings")
@export var mouse_sensitivity: float = 0.005
@export var min_pitch: float = -65.0
@export var max_pitch: float = 60.0

func _ready() -> void:
	var node = get_parent()
	while node:
		if node is CharacterBody3D:
			player_body = node
			break
		node = node.get_parent()

func _unhandled_input(event: InputEvent) -> void:
	if not is_multiplayer_authority():
		return
		
	if event.is_action_pressed("ui_cancel"):
		Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)
		return

	if event is InputEventMouseButton and event.pressed:
		if Input.get_mouse_mode() != Input.MOUSE_MODE_CAPTURED:
			Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
			return

	if Input.get_mouse_mode() != Input.MOUSE_MODE_CAPTURED:
		return

	if event is InputEventMouseMotion:
		_handle_camera_rotation(event)

func _handle_camera_rotation(event: InputEventMouseMotion) -> void:
	if player_body:
		player_body.rotate_y(-event.relative.x * mouse_sensitivity)
	
	rotate_x(-event.relative.y * mouse_sensitivity)
	
	rotation.x = clamp(rotation.x, deg_to_rad(min_pitch), deg_to_rad(max_pitch))
