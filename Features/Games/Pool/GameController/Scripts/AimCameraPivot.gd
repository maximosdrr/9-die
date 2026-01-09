class_name AimCameraPivot
extends Node3D

@onready var elevation_node: Node3D = $Elevation

@export_group("Camera Settings")
@export var mouse_sensitivity: float = 0.005
@export var distance_from_ball: float = 0.55
@export var height_offset: float = 0.1
@export_subgroup("Limits")
@export var min_pitch_deg: float = -25.0
@export var max_pitch_deg: float = 0.0

var _rot_y: float = 0.0
var _rot_x: float = 0.0
var target: Ball

func set_target_ball(ball: Ball):
	target = ball

func _ready() -> void:
	set_as_top_level(true)
	_initialize_rotation()

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	elif event.is_action_pressed("ui_cancel"):
		Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)

	if Input.get_mouse_mode() != Input.MOUSE_MODE_CAPTURED:
		return

	if event is InputEventMouseMotion:
		_rotate_camera(event.relative)

func _process(delta: float) -> void:
	if target == null:
		return

	if target.state_machine.current.type == State.Type.MOVING:
		return
	
	global_position = global_position.\
		lerp(target.global_position, delta * 25.0)

func _initialize_rotation() -> void:
	_rot_y = rotation.y
	if elevation_node:
		_rot_x = elevation_node.rotation.x
		var cam_child = elevation_node.get_child(0)
		if cam_child:
			cam_child.position.z = distance_from_ball
			cam_child.position.y = height_offset

func _rotate_camera(relative_motion: Vector2) -> void:
	_rot_y -= relative_motion.x * mouse_sensitivity
	rotation.y = _rot_y
	
	if elevation_node:
		_rot_x -= relative_motion.y * mouse_sensitivity
		_rot_x = clamp(_rot_x, deg_to_rad(min_pitch_deg), deg_to_rad(max_pitch_deg))
		elevation_node.rotation.x = _rot_x
