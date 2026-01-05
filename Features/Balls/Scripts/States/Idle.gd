extends State
class_name BallIdleState

var stop_speed_threshold: float = 0.01
@export var ball: Ball

func _init() -> void:
	self.type = State.Type.IDLE

func process(_delta: float) -> void:
	if ball.linear_velocity.length() > stop_speed_threshold:
		state_machine.change_state(State.Type.MOVING, {})
	
