class_name PoolGameTable extends StaticBody3D

@onready var score_monitor: PoolScoreMonitor = $ScoreMonitor
@onready var ball_off_monitor: Area3D = $BallOffMonitor


func _on_pockets_detectors_ball_entered(body: Node3D) -> void:
	if body is Ball:
		body.angular_velocity = Vector3.ZERO
		body.linear_velocity = Vector3(0.1, 0, 0.1)
