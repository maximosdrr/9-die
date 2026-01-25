class_name Cue extends Node3D

signal strike_executed(direction: Vector3, force: float, offset: Vector3)

@export_group("References")
@export var state_machine: StateMachine
@export var cue_sfx: CueSfx
@export var stroke_network_bridge: CueNetworkBridge
@export var automatic_elevation: CueAutomaticElevation

@export_group("Physics Config")
@export var max_speed_reference := 12.0
@export var force_multiplier := 8.0
@export var min_force_threshold := 0.01

@export_group("Visual Config")
@export var visual_gap := 0.01
@export var post_shot_cooldown := 0.25

@export_group("Jump Shot Config")
@export var jump_max_angle: float = -65.0
@export var elevation_sensitivity: float = 2.0

var pool_game: PoolGame
var cue_ball: Ball
var camera_pivot: AimCameraPivot

var current_elevation: float = 0.0
var min_safe_angle: float = 0.0

var ball_radius_offset: float = 0.04
var spin_limit: float = 0.02
var spin_offset: Vector2 = Vector2.ZERO

func setup(_pool_game: PoolGame, _camera_pivot: AimCameraPivot) -> void:
	pool_game = _pool_game
	camera_pivot = _camera_pivot
	cue_ball = pool_game.cue_ball

	stroke_network_bridge.setup(self)
	
	#pool_game.turn_changed.connect(_on_turn_changed)
	#pool_game.turn_extended.connect(_on_turn_extended)
	
	_update_ball_limits()
	#_update_turn_state()

func _process(delta: float) -> void:
	var target_rotation_rad = min(deg_to_rad(current_elevation), min_safe_angle)
	rotation.x = lerp(rotation.x, target_rotation_rad, 10.0 * delta)

func execute_strike(mouse_speed: float) -> bool:
	if not _can_strike():
		return false

	var force := _calculate_impulse(mouse_speed)
	
	if force <= min_force_threshold:
		return false

	var strike_data := _get_strike_vectors()
	
	if multiplayer.is_server():
		cue_ball.strike(strike_data.direction, force, strike_data.hit_offset)
	else:
		strike_executed.emit(strike_data.direction, force, strike_data.hit_offset)

	cue_sfx.emit_strike_sound(strike_data.direction, force, strike_data.hit_offset)
	return true

func _calculate_impulse(input_speed: float) -> float:
	var raw_power := clampf(input_speed / max_speed_reference, 0.0, 1.0)
	return pow(raw_power, 2.0) * force_multiplier

func _get_strike_vectors() -> Dictionary:
	var dir := -global_transform.basis.z.normalized()
	var hit_offset := Vector3(spin_offset.x, spin_offset.y, 0.0)
	
	return {
		"direction": dir,
		"hit_offset": hit_offset
	}

func _on_turn_changed(_new_player: String, _context: Dictionary) -> void:
	_update_turn_state()

func _on_turn_extended() -> void:
	_update_turn_state()

func _update_turn_state() -> void:
	current_elevation = 0.0 
	
	if _is_my_turn():
		if state_machine.current.type == StatesRef.CUE_LOCKED:
			state_machine.change_state(StatesRef.CUE_IDLE, {})
	else:
		state_machine.change_state(StatesRef.CUE_LOCKED, {})

func _is_my_turn() -> bool:
	var turn_owner = pool_game.match_manager.turn_owner
	if not turn_owner:
		return false

	var turn_id = int(turn_owner)
	return turn_id == multiplayer.get_unique_id()

func _update_ball_limits() -> void:
	if not cue_ball: return
	ball_radius_offset = cue_ball.radius + visual_gap
	spin_limit = cue_ball.radius

func _can_strike() -> bool:
	return is_instance_valid(cue_ball)
