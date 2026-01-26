class_name AimCameraPivot extends Node3D

@onready var elevation_node: Node3D = $Elevation

@export_group("Camera Behavior")
@export var mouse_sensitivity: float = 0.0015
@export var distance_from_ball: float = 0.55
@export var height_offset: float = 0.1
@export var transition_duration: float = 0.25

@export_group("Rotation Limits")
@export var limit_ceiling_deg: float = -90.0 
@export var limit_floor_deg: float = 15.0 
@export var max_neck_look_up_deg: float = -20.0

@export var cue_offset_deg: float = -5.0

@export_group("Player Positioning")
@export var player_orbit_distance: float = 1.2
@export var player_floor_height: float = 0.0

var cue: Cue 
var _rot_y: float = 0.0
var _rot_x: float = 0.0
var _head_angle: float = 0.0
var _camera_node: Node3D

var target: Ball
var pool_game: PoolGame
var _tween: Tween
var player: Player

func setup(_pool_game: PoolGame, _pool_controller: PoolController):
	target = _pool_game.cue_ball
	pool_game = _pool_game
	cue = _pool_controller.cue
	
	var players = get_tree().get_nodes_in_group(Groups.PLAYER)
	
	for node in players:
		if not node is Player: continue
		if not int(node.name) == multiplayer.get_unique_id(): continue
		player = node
		break
	
	_connect_signals()
	
	if target:
		global_position = target.global_position

func _ready() -> void:
	set_as_top_level(true)
	_initialize_positions()

func _connect_signals() -> void:
	pass
	#var events = [
		#[pool_game.turn_changed, _on_turn_changed],
		#[pool_game.turn_extended, _on_turn_extended],
		#[pool_game.ball_placement_manager.placement_finished, _on_placement_finished]
	#]
	#
	#for event in events:
		#if not event[0].is_connected(event[1]):
			#event[0].connect(event[1])

func _initialize_positions() -> void:
	_rot_y = rotation.y
	if elevation_node:
		_rot_x = elevation_node.rotation.x
		var cam_child = elevation_node.get_child(0)
		if cam_child:
			_camera_node = cam_child
			cam_child.position.z = distance_from_ball
			cam_child.position.y = height_offset

func _physics_process(_delta: float) -> void:
	if not is_multiplayer_authority(): return
	
	var dynamic_limit = _calculate_dynamic_limit()
	
	if _rot_x > dynamic_limit:
		_rot_x = lerp(_rot_x, dynamic_limit, 0.1)
		elevation_node.rotation.x = _rot_x
	
	_sync_player_model_rotation()

func _unhandled_input(event: InputEvent) -> void:
	if not is_multiplayer_authority(): return
	if event is InputEventMouseButton:
		Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	elif event.is_action_pressed("ui_cancel"):
		Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)

	if Input.get_mouse_mode() != Input.MOUSE_MODE_CAPTURED:
		return

	if event is InputEventMouseMotion:
		_apply_rotation(event.relative)

func _apply_rotation(relative_motion: Vector2) -> void:
	_rot_y -= relative_motion.x * mouse_sensitivity
	rotation.y = _rot_y
	
	if not elevation_node: return
	
	_sync_player_model_rotation()

	var delta_mouse = relative_motion.y * mouse_sensitivity
	var current_limit = _calculate_dynamic_limit()
	
	if _head_angle < -0.0001 and delta_mouse < 0:
		_head_angle -= delta_mouse
		
		if _head_angle > 0:
			var remainder = -_head_angle
			_head_angle = 0.0
			_rot_x += remainder
	else:
		_rot_x += delta_mouse
	
	if _rot_x > current_limit:
		var excess = _rot_x - current_limit
		_rot_x = current_limit
		_head_angle -= excess
	
	_head_angle = max(_head_angle, deg_to_rad(max_neck_look_up_deg))
	_rot_x = clamp(_rot_x, deg_to_rad(limit_ceiling_deg), current_limit)
	
	elevation_node.rotation.x = _rot_x
	
	if _camera_node:
		_camera_node.rotation.x = -_head_angle

func _calculate_dynamic_limit() -> float:
	if not cue: return deg_to_rad(limit_floor_deg)
	
	var cue_limit = cue.rotation.x - deg_to_rad(cue_offset_deg)
	
	var floor_limit = deg_to_rad(limit_floor_deg)
	
	return min(cue_limit, floor_limit)

func _on_placement_finished():
	print("cai aqui")
	await get_tree().create_timer(1).timeout
	_move_smoothly_to_target()
	set_process(false)
	set_physics_process(true)

func _on_turn_extended():
	_move_smoothly_to_target()

func _on_turn_changed(_next_player_name: String, _context):
	_move_smoothly_to_target()

func _move_smoothly_to_target():
	if not target: return
	if _tween: _tween.kill()
	_tween = create_tween()
	_tween.set_trans(Tween.TRANS_CUBIC)
	_tween.set_ease(Tween.EASE_OUT)
	_tween.tween_property(self, "global_position", target.global_position, transition_duration)

func _sync_player_model_rotation() -> void:
	if not player: return

	player.global_rotation.y = global_rotation.y

	var direction_back = global_transform.basis.z.normalized()
	var final_pos = global_position + (direction_back * 1.2)
	
	final_pos.y = player_floor_height
	player.global_position = final_pos
