class_name Cue
extends Node3D

@onready var state_machine: StateMachine = $StateMachine

@export_group("References")
@export var cue_sfx: CueSfx
@export var stroke_network_bridge: CueNetworkBridge

@export_group("Power Config")
@export var max_speed_reference: float = 12.0
@export var force_multiplier: float = 8.0
@export var visual_gap: float = 0.01
@export var post_shot_cooldown: float = 0.25

signal strike_executed

var pool_game: PoolGame
var cue_ball: Ball
var camera_pivot: AimCameraPivot

var ball_radius_offset: float = 0.04
var spin_limit: float = 0.02
var spin_offset: Vector2 = Vector2.ZERO

func setup(_pool_game: PoolGame, _camera_pivot: AimCameraPivot) -> void:
	pool_game = _pool_game
	camera_pivot = _camera_pivot
	cue_ball = pool_game.cue_ball

	stroke_network_bridge.setup(self)

	pool_game.turn_changed.connect(_on_turn_change)
	pool_game.turn_extended.connect(_on_turn_extended)
	_sync_turn_state_initial()

func _ready() -> void:
	_update_limits_from_ball()
	_reset_pose_immediate()

func _sync_turn_state_initial() -> void:
	var my_turn := int(pool_game.turn_owner.name) == multiplayer.get_unique_id()
	state_machine.change_state(State.Type.IDLE if my_turn else State.Type.CUE_LOCKED, {})

func _on_turn_change(next_player_name: String, _context) -> void:
	var my_turn := int(next_player_name) == multiplayer.get_unique_id()

	if not my_turn:
		state_machine.change_state(State.Type.CUE_LOCKED, {})
		return

	if state_machine.current.type == State.Type.CUE_LOCKED:
		state_machine.change_state(State.Type.IDLE, {})

func _on_turn_extended() -> void:
	if state_machine.current.type == State.Type.CUE_LOCKED:
		state_machine.change_state(State.Type.IDLE, {})

func execute_strike(impact_speed: float) -> bool:
	if not cue_ball:
		return false

	var raw = clamp(impact_speed / max_speed_reference, 0.0, 1.0)
	var force := pow(raw, 2.0) * force_multiplier
	if force <= 0.01:
		return false

	var dir := -global_transform.basis.z.normalized()
	dir.y = 0.0
	var hit_offset := Vector3(spin_offset.x, spin_offset.y, 0.0)

	if multiplayer.is_server():
		cue_ball.strike(dir, force, hit_offset)
	else:
		strike_executed.emit(dir, force, hit_offset)

	cue_sfx.emit_strike_sound(dir, force, hit_offset)
	return true

func _update_limits_from_ball() -> void:
	if cue_ball and is_inside_tree():
		ball_radius_offset = cue_ball.radius + visual_gap
		spin_limit = cue_ball.radius

func _reset_pose_immediate() -> void:
	position = Vector3(0, 0, ball_radius_offset)
	spin_offset = Vector2.ZERO
