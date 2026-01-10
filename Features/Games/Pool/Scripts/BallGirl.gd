class_name PoolBallGirl extends Node

@export var ball_monitor: Area3D
@export var balls_holder: Node3D

var cue_ball: Ball
var cue_ball_last_pos: Vector3

func setup(_cue_ball: Ball):
	cue_ball = _cue_ball
	cue_ball_last_pos = _cue_ball.global_position
	
	if not cue_ball.stopped_moving.is_connected(_on_cue_ball_stopped):
		cue_ball.stopped_moving.connect(_on_cue_ball_stopped)

func _on_cue_ball_stopped(last_pos: Vector3):
	cue_ball_last_pos = last_pos

func _on_ball_monitor_body_exited(body: Node3D) -> void:
	if not body is Ball:
		return
	
	if body.get_instance_id() != cue_ball.get_instance_id():
		return
	
	cue_ball.linear_velocity = Vector3.ZERO
	cue_ball.angular_velocity = Vector3.ZERO
	cue_ball.global_position = cue_ball_last_pos
