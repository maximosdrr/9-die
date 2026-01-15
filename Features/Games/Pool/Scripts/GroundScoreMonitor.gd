class_name PoolScoreMonitor extends Area3D


func _on_body_entered(body: Node3D) -> void:
	if body is Ball:
		_stop_ball(body)

func _stop_ball(ball: Ball):
	ball.linear_velocity = Vector3.ZERO
	ball.angular_velocity = Vector3.ZERO
	ball.rotation = Vector3.ZERO
