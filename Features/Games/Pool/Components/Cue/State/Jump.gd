class_name CueJumpState extends State

const INPUT_ELEVATION_MODIFIER := "elevation_modifier"

var cue: Cue

func _init() -> void:
	type = State.Type.CUE_JUMPING 

func setup(parent_node: Node3D) -> void:
	cue = parent_node as Cue

func enter(_msg: Dictionary = {}) -> void:
	_set_visual_elevation(cue.min_safe_angle)

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

func process(_delta: float) -> void:
	cue.position.z = cue.ball_radius_offset
	cue.position.x = cue.spin_offset.x
	cue.position.y = cue.spin_offset.y

func _adjust_elevation(direction: int) -> void:
	var step = direction * cue.elevation_sensitivity
	var new_angle = cue.current_elevation - step 
	
	var clamped_angle = clamp(new_angle, cue.jump_max_angle, -40)
	
	_set_visual_elevation(clamped_angle)

func _set_visual_elevation(angle: float) -> void:
	cue.current_elevation = angle
	cue.rotation_degrees.x = cue.current_elevation
