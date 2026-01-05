class_name Table extends StaticBody3D

@onready var poolstick_respawn_marker: Marker3D = $PoolstickRespawnMarker

@export var reference_ball: Ball
@export var stop_speed_threshold: float = 0.05


func _physics_process(_delta: float) -> void:
	if reference_ball == null or poolstick_respawn_marker == null:
		return

	if reference_ball.state_machine.current.type == State.Type.IDLE:
		_move_marker_below_ball()

func _move_marker_below_ball() -> void:
	var origin := reference_ball.global_position
	var to := origin + Vector3.DOWN * 10.0

	var params := PhysicsRayQueryParameters3D.create(origin, to)
	params.exclude = [reference_ball.get_rid()]

	var hit := get_world_3d().direct_space_state.intersect_ray(params)

	if hit.is_empty():
		poolstick_respawn_marker.global_position = origin + Vector3.DOWN * 0.2
	else:
		poolstick_respawn_marker.global_position = hit.position + Vector3.UP * 0.01
