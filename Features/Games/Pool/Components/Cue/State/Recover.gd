class_name CueRecoverState
extends State

var cue: Cue
var _timer: SceneTreeTimer

var _block_until_ms: int = 0

func _init():
	type = State.Type.CUE_RECOVER

func setup(parent_node: Node3D):
	cue = parent_node as Cue

func enter(_m):
	_block_until_ms = Time.get_ticks_msec() + int(cue.post_shot_cooldown * 1000.0)

	cue.create_tween().tween_property(cue, "position:z", cue.ball_radius_offset, 0.2)
	cue.create_tween().tween_property(cue, "spin_offset", Vector2.ZERO, 0.5)

	_timer = cue.get_tree().create_timer(cue.post_shot_cooldown)
	_timer.timeout.connect(func():
		state_machine.change_state(State.Type.CUE_LOCKED, {})
	)

func exit(_m):
	_timer = null

func handle_input(event: InputEvent) -> void:
	if Time.get_ticks_msec() < _block_until_ms:
		if event is InputEventMouseMotion:
			get_viewport().set_input_as_handled()
			return
