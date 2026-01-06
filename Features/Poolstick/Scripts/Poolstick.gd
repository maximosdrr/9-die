class_name Poolstick extends Node3D

@export var poolstick_body: Node3D
@export var cue_ball: Ball = null:
	set(value):
		cue_ball = value

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_released("can_strike"):
		_execute_strike()

func _execute_strike() -> void:
	var _current_power = 0.6
	if _current_power <= 0.1:
		return
		
	var strike_direction = (cue_ball.global_position - poolstick_body.global_position).normalized()
	strike_direction.y = 0 
	cue_ball.strike(strike_direction, _current_power)
