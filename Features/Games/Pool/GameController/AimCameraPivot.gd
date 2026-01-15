class_name AimCameraPivot
extends Node3D

@onready var elevation_node: Node3D = $Elevation

@export_group("Camera Settings")
@export var mouse_sensitivity: float = 0.005
@export var distance_from_ball: float = 0.55
@export var height_offset: float = 0.1
@export var transition_duration: float = 0.25

@export_subgroup("Limits")
@export var min_pitch_deg: float = -25.0
@export var max_pitch_deg: float = 0.0

var curren_max_pitch_deg = 0.0

var _rot_y: float = 0.0
var _rot_x: float = 0.0
var target: Ball
var pool_game: PoolGame
var _tween: Tween

func setup(_pool_game: PoolGame):
	target = _pool_game.cue_ball
	pool_game = _pool_game
	
	if not pool_game.turn_changed.is_connected(_on_turn_changed):
		pool_game.turn_changed.connect(_on_turn_changed)
	if not pool_game.match_started.is_connected(_on_match_started):
		pool_game.match_started.connect(_on_match_started)
	if not pool_game.turn_extended.is_connected(_on_turn_extended):
		pool_game.turn_extended.connect(_on_turn_extended)
	if not pool_game.ball_placement_manager.placement_finished.is_connected(_on_placement_finished):
		pool_game.ball_placement_manager.placement_finished.connect(_on_placement_finished)

func _ready() -> void:
	curren_max_pitch_deg = max_pitch_deg
	set_as_top_level(true)
	_initialize_rotation()
	set_process(false)

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	elif event.is_action_pressed("ui_cancel"):
		Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)

	if Input.get_mouse_mode() != Input.MOUSE_MODE_CAPTURED:
		return

	if event is InputEventMouseMotion:
		_rotate_camera(event.relative)

func _on_placement_finished():
	await get_tree().create_timer(1).timeout
	_move_smoothly_to_target()

func _on_turn_extended():
	_move_smoothly_to_target()

func _on_turn_changed(_next_player_name: String, _context):
	_move_smoothly_to_target()

func _on_match_started(_players_ids: Array, _first_turn_player: String):
	if target:
		global_position = target.global_position

func _move_smoothly_to_target():
	if not target: return
	
	if _tween: _tween.kill()
	
	_tween = create_tween()
	
	_tween.set_trans(Tween.TRANS_CUBIC)
	_tween.set_ease(Tween.EASE_OUT)
	
	_tween.tween_property(self, "global_position", target.global_position, transition_duration)


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
		_rot_x = clamp(_rot_x, deg_to_rad(min_pitch_deg), deg_to_rad(curren_max_pitch_deg))
		elevation_node.rotation.x = _rot_x
