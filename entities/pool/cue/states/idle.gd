class_name CueIdleState
extends State

const INPUT_SPIN_MODIFIER := "spin_modifier"
const INPUT_STROKE_MODE := "stroke_mode"
const INPUT_ELEVATION_MODIFIER := "elevation_modifier"

var cue: Cue

func _init() -> void:
	type = StatesRef.CUE_IDLE

func setup(parent_node: Node3D) -> void:
	cue = parent_node as Cue

func enter(_msg: Dictionary = {}) -> void:
	_update_cue_pose()

func handle_input(event: InputEvent) -> void:
	if not cue.is_multiplayer_authority():
		return

	if event.is_action_pressed(INPUT_SPIN_MODIFIER):
		state_machine.change_state(StatesRef.CUE_SPINNING, {})
		get_viewport().set_input_as_handled()
		return

	if event.is_action_pressed(INPUT_STROKE_MODE):
		state_machine.change_state(StatesRef.CUE_CHARGING, {})
		get_viewport().set_input_as_handled()
		return
	
	if event.is_action_pressed(INPUT_ELEVATION_MODIFIER):
		state_machine.change_state(StatesRef.CUE_JUMPING, {})
		get_viewport().set_input_as_handled()
		return

func process(_delta: float) -> void:
	_update_cue_pose()

func _update_cue_pose() -> void:
	cue.position.z = cue.ball_radius_offset
	cue.position.x = cue.spin_offset.x
	cue.position.y = cue.spin_offset.y
