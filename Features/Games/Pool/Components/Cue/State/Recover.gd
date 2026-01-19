class_name CueRecoverState
extends State

var cue: Cue
var _tween: Tween

func _init():
	type = State.Type.CUE_RECOVER

func setup(parent_node: Node3D):
	cue = parent_node as Cue

func enter(_m):
	if _tween: _tween.kill()
	
	_tween = cue.create_tween()
	_tween.set_parallel(true)
	_tween.tween_property(cue, "position:z", cue.ball_radius_offset, 0.2).set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_QUAD)
	_tween.tween_property(cue, "spin_offset", Vector2.ZERO, 0.5).set_ease(Tween.EASE_OUT)
	_reset_elevation()
	_tween.set_parallel(false)
	
	var time_to_wait = max(cue.post_shot_cooldown, 0.5)
	
	_tween.tween_interval(time_to_wait - 0.5)
	_tween.tween_callback(_on_cooldown_finished)

func exit(_m):
	if _tween:
		_tween.kill()
		_tween = null

func handle_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		if cue.get_viewport():
			cue.get_viewport().set_input_as_handled()

func _on_cooldown_finished():
	state_machine.change_state(State.Type.CUE_LOCKED, {})

func _reset_elevation() -> void:
	cue.current_elevation = 0.0
	cue.rotation_degrees.x = 0.0
