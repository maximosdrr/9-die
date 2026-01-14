extends State

class_name BallMovingState

var stop_speed_threshold: float = 0.01
var stop_check_timer_value := 0.1
var stop_check_timer := stop_check_timer_value

@export var ball: Ball

func _init() -> void:
	self.type = State.Type.MOVING
	

func physics_process(delta: float) -> void:
	stop_check_timer -= delta
	
	if stop_check_timer > 0:
		return
	
	stop_check_timer = stop_check_timer_value
	
	if ball.linear_velocity.length() <= stop_speed_threshold:
		state_machine.change_state(State.Type.IDLE, {})
		ball.stopped_moving.emit(ball.global_position)
	
