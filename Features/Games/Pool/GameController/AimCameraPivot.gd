class_name AimCameraPivot
extends Node3D

@onready var elevation_node: Node3D = $Elevation

@export_group("Camera Settings")
@export var mouse_sensitivity: float = 0.0025
@export var distance_from_ball: float = 0.55
@export var height_offset: float = 0.05

@export var head_tilt_offset_deg: float = -20.0 
@export var transition_duration: float = 0.25

@export_subgroup("Limits")
@export var min_pitch_deg: float = -90.0
@export var max_head_look_up_deg: float = -60.0

var cue: Cue 

var curren_max_pitch_deg = 0.0
var _rot_y: float = 0.0
var _rot_x: float = 0.0
var _head_look_angle: float = 0.0
var _camera_node: Node3D

var target: Ball
var pool_game: PoolGame
var _tween: Tween

func setup(_pool_game: PoolGame, _pool_controller: PoolController):
	target = _pool_game.cue_ball
	pool_game = _pool_game
	cue = _pool_controller.cue
	
	if not pool_game.turn_changed.is_connected(_on_turn_changed):
		pool_game.turn_changed.connect(_on_turn_changed)
	if not pool_game.match_started.is_connected(_on_match_started):
		pool_game.match_started.connect(_on_match_started)
	if not pool_game.turn_extended.is_connected(_on_turn_extended):
		pool_game.turn_extended.connect(_on_turn_extended)
	if not pool_game.ball_placement_manager.placement_finished.is_connected(_on_placement_finished):
		pool_game.ball_placement_manager.placement_finished.connect(_on_placement_finished)

func _ready() -> void:
	set_as_top_level(true)
	_initialize_rotation()
	set_process(false)

func _physics_process(_delta: float) -> void:
	# Não atualizamos o taco aqui. Apenas reagimos a ele.
	_enforce_physical_limits()

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
	set_process(false)
	set_physics_process(true)

func _on_turn_extended():
	_move_smoothly_to_target()

func _on_turn_changed(_next_player_name: String, _context):
	_move_smoothly_to_target()

func _on_match_started(_players_ids: Array, _first_turn_player: String):
	if target:
		global_position = target.global_position
		set_physics_process(true)

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
			_camera_node = cam_child
			cam_child.position.z = distance_from_ball
			cam_child.position.y = height_offset

func _rotate_camera(relative_motion: Vector2) -> void:
	_rot_y -= relative_motion.x * mouse_sensitivity
	rotation.y = _rot_y
	
	if elevation_node:
		var delta_y = relative_motion.y * mouse_sensitivity
		var current_limit = _calculate_dynamic_limit()
		
		# Lógica de Input da Câmera
		if _head_look_angle < -0.0001 and delta_y < 0:
			_head_look_angle -= delta_y 
			if _head_look_angle > 0:
				var remainder = -_head_look_angle
				_head_look_angle = 0.0
				_rot_x += remainder
		else:
			_rot_x += delta_y
		
		if _rot_x > current_limit:
			var excess = _rot_x - current_limit
			_rot_x = current_limit
			_head_look_angle -= excess
		
		_head_look_angle = max(_head_look_angle, deg_to_rad(max_head_look_up_deg))
		_rot_x = clamp(_rot_x, deg_to_rad(min_pitch_deg), current_limit)
		
		elevation_node.rotation.x = _rot_x
		
		if _camera_node:
			_camera_node.rotation.x = -_head_look_angle

func _calculate_dynamic_limit() -> float:
	if not cue: return 0.0
	
	var current_cue_pitch = cue.rotation.x
	var head_offset_rad = deg_to_rad(head_tilt_offset_deg)
	
	var dynamic_limit = current_cue_pitch - head_offset_rad
	return min(dynamic_limit, 0.3)

func _enforce_physical_limits() -> void:
	# Garante que se o taco subir sozinho, a câmera sobe junto
	var limit = _calculate_dynamic_limit()
	if _rot_x > limit:
		_rot_x = lerp(_rot_x, limit, 0.1)
		elevation_node.rotation.x = _rot_x
