class_name CueJumpState extends State

const INPUT_ELEVATION_MODIFIER := "elevation_modifier"

var cue: Cue

func _init() -> void:
	type = State.Type.CUE_JUMPING 

func setup(parent_node: Node3D) -> void:
	cue = parent_node as Cue

func enter(_msg: Dictionary = {}) -> void:
	var safe_angle_deg = rad_to_deg(cue.min_safe_angle)
	_set_visual_elevation(safe_angle_deg)

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
	
	var safe_limit_deg = rad_to_deg(cue.min_safe_angle)
	var clamped_angle = clamp(new_angle, cue.jump_max_angle, safe_limit_deg)
	
	_set_visual_elevation(clamped_angle)

func _set_visual_elevation(angle: float) -> void:
	cue.current_elevation = angle
	cue.rotation_degrees.x = cue.current_elevation
