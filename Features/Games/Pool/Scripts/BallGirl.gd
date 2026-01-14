class_name OffTableMonitor extends Node

@export var ball_off_monitor: Area3D

signal ball_fell_off(ball: Ball)

func _ready() -> void:
	ball_off_monitor.body_entered.connect(_on_body_entered)

func _on_body_entered(body: Node) -> void:
	if body is Ball:
		ball_fell_off.emit(body)
