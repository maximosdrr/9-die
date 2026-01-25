class_name OffTableMonitor extends Node

var ball_off_monitor: Area3D
signal ball_fell_off(ball: Ball)

func setup(_ball_off_monitor: Area3D):
	ball_off_monitor = _ball_off_monitor
	ball_off_monitor.body_entered.connect(_on_body_entered)

func _on_body_entered(body: Node) -> void:
	if body is Ball:
		ball_fell_off.emit(body)
