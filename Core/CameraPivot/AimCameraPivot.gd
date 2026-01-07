class_name AimCameraPivot
extends Node3D

# --- CONFIGURATION ---
@export var target: Node3D
@export var elevation_node: Node3D

@export_group("Settings")
@export var mouse_sensitivity := 0.005
@export var min_pitch_deg := -65.0
@export var max_pitch_deg := 60.0
@export var distance_from_ball := 0.55
@export var height_offset := 0.1

var _rotation_y: float = 0.0
var _rotation_x: float = 0.0

func _ready() -> void:
	# Initialize rotation based on current state
	_rotation_y = rotation.y
	if elevation_node:
		_rotation_x = elevation_node.rotation.x
		# Apply initial distance to the Remote (or child)
		# Assuming RemoteAim is the first child of Elevation
		var remote = elevation_node.get_child(0)
		if remote:
			remote.position.z = distance_from_ball
			remote.position.y = height_offset

func _unhandled_input(event: InputEvent) -> void:
	# Toggle Mouse Capture
	if event is InputEventMouseButton:
		Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	elif event.is_action_pressed("ui_cancel"):
		Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)

	if Input.get_mouse_mode() != Input.MOUSE_MODE_CAPTURED:
		return

	if event is InputEventMouseMotion:
		# 1. Yaw (Left/Right) - Rotates THIS Node (The Pivot)
		_rotation_y -= event.relative.x * mouse_sensitivity
		rotation.y = _rotation_y
		
		# 2. Pitch (Up/Down) - Rotates the Child (Elevation)
		if elevation_node:
			_rotation_x -= event.relative.y * mouse_sensitivity
			_rotation_x = clamp(_rotation_x, deg_to_rad(min_pitch_deg), deg_to_rad(max_pitch_deg))
			elevation_node.rotation.x = _rotation_x

func _physics_process(delta: float) -> void:
	if target:
		global_position = global_position.lerp(target.global_position, delta * 25.0)
