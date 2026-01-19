class_name CueJumpState
extends State

const INPUT_ELEVATION_MODIFIER := "elevation_modifier"
const INPUT_WHEEL_UP := "wheel_up"
const INPUT_WHEEL_DOWN := "wheel_down"

var cue: Cue

func _init() -> void:
	type = State.Type.CUE_JUMPING 

func setup(parent_node: Node3D) -> void:
	cue = parent_node as Cue

func _adjust_elevation(dir: int) -> void:
	var new_elevation = cue.current_elevation + (dir * cue.elevation_sensitivity)
	cue.current_elevation = clamp(new_elevation, cue.min_elevation_deg, cue.max_elevation_deg)
	cue.rotation_degrees.x = cue.current_elevation

func handle_input(event: InputEvent) -> void:
	if not cue.is_multiplayer_authority():
		return

	if event.is_action_released(INPUT_ELEVATION_MODIFIER):
		state_machine.change_state(State.Type.IDLE, {})
		get_viewport().set_input_as_handled()
		return

	if event is InputEventMouseButton and event.is_pressed():
		if event.button_index == MOUSE_BUTTON_WHEEL_UP:
			_adjust_elevation(1)
			get_viewport().set_input_as_handled()
			return
			
		if event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			_adjust_elevation(-1)
			get_viewport().set_input_as_handled()
			return

	if event is InputEventMouseMotion:
		pass

func process(_delta: float) -> void:
	cue.position.z = cue.ball_radius_offset
	cue.position.x = cue.spin_offset.x
	cue.position.y = cue.spin_offset.y
