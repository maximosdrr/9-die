class_name CueIdleState extends State

@export var spin_sensitivity := 0.001

var cue: Cue
var _spin_modifier_down := false

func _init():
	type = State.Type.IDLE

func setup(parent_node: Node3D):
	cue = parent_node as Cue

func enter(_m):
	_spin_modifier_down = false
	cue.position.z = cue.ball_radius_offset
	_apply_spin_to_pose()

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

	if event.is_action_pressed("stroke_mode"):
		state_machine.change_state(State.Type.CUE_CHARGING, {})
		get_viewport().set_input_as_handled()
		return

	if event is InputEventMouseMotion and _spin_modifier_down:
		_apply_spin_motion(event.relative)
		_apply_spin_to_pose()
		get_viewport().set_input_as_handled()

func process(_delta):
	cue.position.z = cue.ball_radius_offset
	_apply_spin_to_pose()

func _apply_spin_motion(rel: Vector2):
	var scaled := rel * spin_sensitivity
	cue.spin_offset.x += scaled.x
	cue.spin_offset.y += -scaled.y

	if cue.spin_offset.length() > cue.spin_limit:
		cue.spin_offset = cue.spin_offset.normalized() * cue.spin_limit

func _apply_spin_to_pose():
	cue.position.x = cue.spin_offset.x
	cue.position.y = cue.spin_offset.y
