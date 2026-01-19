class_name CueChargingState
extends State

const INPUT_SPIN_MODIFIER := "spin_modifier"
const INPUT_STROKE_MODE := "stroke_mode"

@export_group("Sensitivity")
@export var spin_sensitivity := 0.001
@export var stroke_sensitivity := 0.001

@export_group("Physics")
@export var max_draw_distance := 0.35
@export var strike_contact_threshold := 0.001
@export var strike_velocity_threshold := -0.10
@export var velocity_smoothing := 18.0

var cue: Cue

var _current_draw_distance: float
var _previous_draw_distance: float
var _smoothed_velocity: float

func _init() -> void:
	type = State.Type.CUE_CHARGING

func setup(parent_node: Node3D) -> void:
	cue = parent_node as Cue

func enter(_msg: Dictionary = {}) -> void:
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	
	_current_draw_distance = max(cue.position.z, cue.ball_radius_offset)
	_previous_draw_distance = _current_draw_distance
	_smoothed_velocity = 0.0

func exit(_msg: Dictionary = {}) -> void:
	pass

func handle_input(event: InputEvent) -> void:
	if not cue.is_multiplayer_authority():
		return

	if event.is_action_released(INPUT_STROKE_MODE):
		state_machine.change_state(State.Type.IDLE, {})
		get_viewport().set_input_as_handled()
		return

	if event is InputEventMouseMotion:
		var is_spin_mode = Input.is_action_pressed(INPUT_SPIN_MODIFIER)
		
		if is_spin_mode:
			_process_spin_input(event.relative)
		else:
			_process_stroke_input(event.relative)

		_update_cue_transform()
		get_viewport().set_input_as_handled()

func process(delta: float) -> void:
	_calculate_velocity(delta)

	if _check_strike_condition():
		var strike_power := absf(_smoothed_velocity)
		var success := cue.execute_strike(strike_power)
		
		var next_state = State.Type.CUE_RECOVER if success else State.Type.IDLE
		state_machine.change_state(next_state, {})

func _process_spin_input(relative_motion: Vector2) -> void:
	var motion_delta := relative_motion * spin_sensitivity
	
	cue.spin_offset.x += motion_delta.x
	cue.spin_offset.y -= motion_delta.y
	
	cue.spin_offset = cue.spin_offset.limit_length(cue.spin_limit)

func _process_stroke_input(relative_motion: Vector2) -> void:
	var draw_delta := relative_motion.y * stroke_sensitivity
	var min_draw := cue.ball_radius_offset
	var max_draw := cue.ball_radius_offset + max_draw_distance
	
	_current_draw_distance = clamp(_current_draw_distance + draw_delta, min_draw, max_draw)

func _update_cue_transform() -> void:
	cue.position.x = cue.spin_offset.x
	cue.position.y = cue.spin_offset.y
	cue.position.z = _current_draw_distance

func _calculate_velocity(delta: float) -> void:
	if delta <= 0.0:
		return

	var instant_velocity := (_current_draw_distance - _previous_draw_distance) / delta
	_previous_draw_distance = _current_draw_distance

	var weight := clampf(delta * velocity_smoothing, 0.0, 1.0)
	_smoothed_velocity = lerpf(_smoothed_velocity, instant_velocity, weight)

func _check_strike_condition() -> bool:
	var is_touching_ball = _current_draw_distance <= (cue.ball_radius_offset + strike_contact_threshold)
	var is_moving_forward = _smoothed_velocity < strike_velocity_threshold
	
	return is_touching_ball and is_moving_forward
