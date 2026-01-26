class_name BallsMovementMonitor extends Node

@export var ball_check_delay := 0.1
@export var stop_tolerance := 0.5

var pool_game: PoolGame
var balls: Array[Ball]

var _ball_check_timer = 0.0
var _stop_tolerance_timer = 0.0
var is_moving_state = false

signal balls_stopped
signal balls_moving

func _ready() -> void:
	set_process(false)

func setup(_pool_game: PoolGame):
	pool_game = _pool_game
	balls = _pool_game.balls.duplicate()
	balls.append(pool_game.cue_ball)
	set_process(true)

func _process(delta: float) -> void:
	if _ball_check_timer > 0:
		_ball_check_timer -= delta
		return
	
	_ball_check_timer = ball_check_delay
	
	var any_ball_moving = false
	
	for ball in balls:
		if not is_instance_valid(ball):
			continue

		if ball.linear_velocity.length() > 0.01:
			any_ball_moving = true
			break

	if any_ball_moving:
		_stop_tolerance_timer = stop_tolerance
		
		if not is_moving_state:
			is_moving_state = true
			balls_moving.emit()
	else:
		if is_moving_state:
			_stop_tolerance_timer -= ball_check_delay
			
			if _stop_tolerance_timer <= 0:
				is_moving_state = false
				balls_stopped.emit()
