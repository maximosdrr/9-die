class_name Table extends StaticBody3D

@onready var ball_stop_position: Marker3D = $BallStopPosition
@onready var aim: Aim = $BallStopPosition/Aim

@export var cue_ball: Ball
@export var stop_speed_threshold: float = 0.05


func _physics_process(_delta: float) -> void:
	if cue_ball == null or ball_stop_position == null:
		return

	if cue_ball.state_machine.current.type == State.Type.IDLE:
		_move_marker_below_ball()

func _move_marker_below_ball() -> void:
	var origin := cue_ball.global_position
	var to := origin + Vector3.DOWN * 10.0

	var params := PhysicsRayQueryParameters3D.create(origin, to)
	params.exclude = [cue_ball.get_rid()]

	var hit := get_world_3d().direct_space_state.intersect_ray(params)

	if hit.is_empty():
		ball_stop_position.global_position = origin + Vector3.DOWN * 0.2
	else:
		ball_stop_position.global_position = hit.position + Vector3.UP * 0.01
