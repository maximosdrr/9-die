class_name CueInputSystem extends Node

@export_group("Settings")
@export var input_scale: float = 1.0
@export var invert_y_axis: bool = false

var cue: Cue
var is_adjusting_spin: bool = false

func setup(_cue: Cue):
	cue = _cue

func _ready() -> void:
	if not cue and get_parent() is Cue:
		cue = get_parent()

func _unhandled_input(event: InputEvent) -> void:
	if not is_multiplayer_authority():
		return
	
	if not cue: return
	
	if cue.is_locked:
		if Input.mouse_mode != Input.MOUSE_MODE_CAPTURED:
			Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
		get_viewport().set_input_as_handled()
		return
	
	if event.is_action_pressed("spin_modifier"):
		if not cue.is_charging:
			is_adjusting_spin = true
			Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
		get_viewport().set_input_as_handled()
	
	elif event.is_action_released("spin_modifier"):
		is_adjusting_spin = false
		get_viewport().set_input_as_handled()

	if event.is_action_pressed("stroke_mode"):
		if not is_adjusting_spin:
			cue.start_charging()
			Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
		get_viewport().set_input_as_handled()
		
	elif event.is_action_released("stroke_mode"):
		if cue.is_charging:
			cue.cancel_charging()
			get_viewport().set_input_as_handled()

	if event is InputEventMouseMotion:
		_handle_mouse_motion(event)

func _handle_mouse_motion(event: InputEventMouseMotion) -> void:
	var relative_motion = event.relative * input_scale
	
	if is_adjusting_spin:
		cue.process_spin_input(relative_motion)
		get_viewport().set_input_as_handled()
		
	elif cue.is_charging:
		var stroke_delta = relative_motion.y
		if invert_y_axis: stroke_delta = -stroke_delta
		cue.process_stroke_input(stroke_delta)
		get_viewport().set_input_as_handled()
