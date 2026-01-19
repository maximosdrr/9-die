class_name CueChargingState
extends State

@export var spin_sensitivity := 0.001
@export var stroke_sensitivity := 0.001
@export var max_draw_distance := 0.35
@export var strike_z_eps := 0.001
@export var strike_vel_threshold := -0.10
@export var avg_vel_smoothing := 18.0

var cue: Cue

var draw_z: float
var _prev_draw_z: float
var _avg_velocity: float
var _spin_modifier_down := false

func _init():
	type = State.Type.CUE_CHARGING

func setup(parent_node: Node3D):
	cue = parent_node as Cue

func enter(_m):
	_spin_modifier_down = false
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

	draw_z = max(cue.position.z, cue.ball_radius_offset)
	_prev_draw_z = draw_z
	_avg_velocity = 0.0

func exit(_m):
	_spin_modifier_down = false

func handle_input(event: InputEvent) -> void:
	if not cue.is_multiplayer_authority():
		return

	if event.is_action_pressed("spin_modifier"):
		_spin_modifier_down = true
		get_viewport().set_input_as_handled()
		return

	if event.is_action_released("spin_modifier"):
		_spin_modifier_down = false
		get_viewport().set_input_as_handled()
		return

	if event.is_action_released("stroke_mode"):
		state_machine.change_state(State.Type.IDLE, {})
		get_viewport().set_input_as_handled()
		return

	if event is InputEventMouseMotion:
		if _spin_modifier_down:
			_apply_spin_motion(event.relative)
		else:
			_apply_stroke_motion(event.relative)

		_apply_spin_to_pose()
		cue.position.z = draw_z
		get_viewport().set_input_as_handled()

func process(delta: float):
	_update_velocity(delta)

	if _should_strike():
		var ok := cue.execute_strike(abs(_avg_velocity))
		state_machine.change_state(State.Type.CUE_RECOVER if ok else State.Type.IDLE, {})

func _apply_spin_motion(rel: Vector2):
	var scaled := rel * spin_sensitivity
	cue.spin_offset.x += scaled.x
	cue.spin_offset.y += -scaled.y

	if cue.spin_offset.length() > cue.spin_limit:
		cue.spin_offset = cue.spin_offset.normalized() * cue.spin_limit

func _apply_stroke_motion(rel: Vector2):
	var dy := rel.y * stroke_sensitivity
	draw_z = clamp(draw_z + dy, cue.ball_radius_offset, cue.ball_radius_offset + max_draw_distance)

func _update_velocity(delta: float):
	if delta <= 0.0:
		return

	var v := (draw_z - _prev_draw_z) / delta
	_prev_draw_z = draw_z

	var t = clamp(delta * avg_vel_smoothing, 0.0, 1.0)
	_avg_velocity = lerp(_avg_velocity, v, t)

func _should_strike() -> bool:
	return draw_z <= (cue.ball_radius_offset + strike_z_eps) and _avg_velocity < strike_vel_threshold

func _apply_spin_to_pose():
	cue.position.x = cue.spin_offset.x
	cue.position.y = cue.spin_offset.y
