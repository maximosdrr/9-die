class_name Poolstick extends Node3D

@export_group("Stroke Settings")
@export var stroke_sensitivity: float = 0.01 
@export var max_draw_distance: float = 1.5
@export var ball_radius_offset: float = 0.06 
@export var max_speed_reference: float = 6

var _is_charging: bool = false
var _previous_z: float = 0.0
var _stick_velocity: float = 0.0
var _accumulated_mouse_y: float = 0.0

var cue_ball: Ball

func set_cue_ball(ball: Ball):
	cue_ball = ball

func _ready() -> void:
	position.z = ball_radius_offset
	_previous_z = position.z

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("stroke_mode"):
		_start_charging()
		get_viewport().set_input_as_handled()
	
	elif event.is_action_released("stroke_mode"):
		_cancel_charging()
		return

	if _is_charging and event is InputEventMouseMotion:
		_accumulated_mouse_y += event.relative.y
		get_viewport().set_input_as_handled()

func _process(delta: float) -> void:
	if not _is_charging:
		return
		
	_apply_movement_logic()
	
	var current_z = position.z
	_stick_velocity = (current_z - _previous_z) / delta
	_previous_z = current_z
	
	if current_z <= (ball_radius_offset + 0.001) and _stick_velocity < -0.1:
		_execute_strike()

func _start_charging() -> void:
	_is_charging = true
	_accumulated_mouse_y = 0.0

func _cancel_charging() -> void:
	_is_charging = false
	_reset_animation()

func _apply_movement_logic() -> void:
	if _accumulated_mouse_y == 0: return
		
	var target_z = position.z + (_accumulated_mouse_y * stroke_sensitivity)
	target_z = clamp(target_z, ball_radius_offset, max_draw_distance)
	
	position.z = target_z
	_accumulated_mouse_y = 0.0

func _execute_strike() -> void:
	if not cue_ball: return

	var impact_speed = abs(_stick_velocity)
	
	var raw_power = clamp(impact_speed / max_speed_reference, 0.0, 1.0)
	var curved_power = pow(raw_power, 2.0) 
	
	print("Speed: %.2f | Raw: %.2f | Curved: %.2f" % [impact_speed, raw_power, curved_power])
	
	var dir = -global_transform.basis.z.normalized()
	dir.y = 0 
	
	cue_ball.strike(dir, curved_power)
	
	_is_charging = false
	position.z = ball_radius_offset

func _reset_animation() -> void:
	var tween = create_tween()
	tween.tween_property(self, "position:z", ball_radius_offset, 0.2).set_trans(Tween.TRANS_SINE)
