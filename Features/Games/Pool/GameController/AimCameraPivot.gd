class_name AimCameraPivot
extends Node3D

@onready var elevation_node: Node3D = $Elevation

@export_group("Camera Settings")
@export var mouse_sensitivity: float = 0.005
@export var distance_from_ball: float = 0.55
@export var height_offset: float = 0.1

@export var head_tilt_offset_deg: float = 5.0 
@export var transition_duration: float = 0.25

@export_subgroup("Limits")
@export var min_pitch_deg: float = -45.0
@export var max_pitch_deg: float = -5.0

var cue: Cue
var cue_handle_sensor: RayCast3D
var cue_length: float = 1.47 
var extra_height_margin: float = 0.08 

var curren_max_pitch_deg = 0.0
var _rot_y: float = 0.0
var _rot_x: float = 0.0
var target: Ball
var pool_game: PoolGame
var _tween: Tween

func setup(_pool_game: PoolGame, _pool_controller: PoolController):
	target = _pool_game.cue_ball
	pool_game = _pool_game
	cue = _pool_controller.cue
	
	# Setup do Sensor de Colisão do Taco (Restaurado)
	cue_handle_sensor = get_node_or_null("CueHandleSensor")
	
	if not cue_handle_sensor:
		push_warning("ATENÇÃO: RayCast 'CueHandleSensor' não encontrado como filho do AimPivot!")
	else:
		# Garante posição correta (Atrás e Alto)
		cue_handle_sensor.position = Vector3(0, 0.5, cue_length)
		cue_handle_sensor.target_position = Vector3(0, -2.0, 0)
	
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

func _physics_process(_delta: float) -> void:
	# 1. Atualiza a física do Taco (Raycast)
	_update_cue_angle()
	
	# 2. Atualiza a limitação da Câmera baseada no Taco
	_update_camera_dynamic_limit()

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
			cam_child.position.z = distance_from_ball
			cam_child.position.y = height_offset

func _rotate_camera(relative_motion: Vector2) -> void:
	_rot_y -= relative_motion.x * mouse_sensitivity
	rotation.y = _rot_y
	
	# Apenas aplica o input do mouse. 
	# A restrição física acontece no _physics_process para ser mais suave.
	if elevation_node:
		_rot_x -= relative_motion.y * mouse_sensitivity
		
		# Clamp inicial básico (será sobrescrito pelo dinâmico se necessário)
		_rot_x = clamp(_rot_x, deg_to_rad(min_pitch_deg), deg_to_rad(max_pitch_deg))
		elevation_node.rotation.x = _rot_x

# --- LÓGICA 1: CALCULAR ÂNGULO DO TACO (FÍSICA) ---
func _update_cue_angle() -> void:
	if not cue or not cue_handle_sensor: return
	
	var target_cue_pitch = 0.0
	
	if cue_handle_sensor.is_colliding():
		var collision_point = cue_handle_sensor.get_collision_point()
		var diff_y = (collision_point.y + extra_height_margin) - global_position.y
		
		if diff_y > 0:
			# Calcula distância real 2D para ângulo correto
			var pivot_pos_2d = Vector2(global_position.x, global_position.z)
			var col_pos_2d = Vector2(collision_point.x, collision_point.z)
			var dist = pivot_pos_2d.distance_to(col_pos_2d)
			dist = max(dist, 0.1)
			
			var angle_rad = atan2(diff_y, dist)
			target_cue_pitch = -abs(angle_rad)
	
	target_cue_pitch = clamp(target_cue_pitch, deg_to_rad(-45.0), 0.0)
	cue.rotation.x = lerp(cue.rotation.x, target_cue_pitch, 0.2)

func _update_camera_dynamic_limit() -> void:
	if not elevation_node or not cue: return
	
	var current_cue_pitch = cue.rotation.x
	var head_offset_rad = deg_to_rad(head_tilt_offset_deg)
	
	var dynamic_limit = current_cue_pitch - head_offset_rad
	var final_limit = min(dynamic_limit, deg_to_rad(max_pitch_deg))
	
	if _rot_x > final_limit:
		_rot_x = lerp(_rot_x, final_limit, 0.1)
		elevation_node.rotation.x = _rot_x
