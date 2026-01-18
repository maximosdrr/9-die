class_name CueRecoverState extends State

var cue: Cue
var _timer: SceneTreeTimer

func _init():
	type = State.Type.CUE_RECOVER

func setup(parent_node: Node3D):
	cue = parent_node as Cue

func enter(_m):
	cue.create_tween().tween_property(cue, "position:z", cue.ball_radius_offset, 0.2)
	cue.create_tween().tween_property(cue, "spin_offset", Vector2.ZERO, 0.5)

	_timer = cue.get_tree().create_timer(cue.post_shot_cooldown)
	_timer.timeout.connect(func():
		state_machine.change_state(State.Type.CUE_LOCKED, {})
	)

func exit(_m):
	_timer = null
