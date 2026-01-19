class_name CueSpinState extends State

const INPUT_SPIN_MODIFIER := "spin_modifier"
@export var spin_sensitivity := 0.001

var cue: Cue

func _init() -> void:
	type = State.Type.CUE_SPINNING 

func setup(parent_node: Node3D) -> void:
	cue = parent_node as Cue

func handle_input(event: InputEvent) -> void:
	if not cue.is_multiplayer_authority():
		return

	if event.is_action_released(INPUT_SPIN_MODIFIER):
		state_machine.change_state(State.Type.IDLE, {})
		get_viewport().set_input_as_handled()
		return

	if event is InputEventMouseMotion:
		_process_spin_input(event.relative)
		_update_cue_pose()
		get_viewport().set_input_as_handled()

func process(_delta: float) -> void:
	_update_cue_pose()

func _process_spin_input(relative_motion: Vector2) -> void:
	var motion_delta := relative_motion * spin_sensitivity
	
	cue.spin_offset.x += motion_delta.x
	cue.spin_offset.y -= motion_delta.y
	
	cue.spin_offset = cue.spin_offset.limit_length(cue.spin_limit)

func _update_cue_pose() -> void:
	cue.position.x = cue.spin_offset.x
	cue.position.y = cue.spin_offset.y
	cue.position.z = cue.ball_radius_offset
