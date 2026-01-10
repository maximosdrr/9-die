class_name CueSpinSystem extends Node

@export_group("Spin Settings")
@export var sensitivity: float = 0.0005
@export_range(0.0, 1.0) var max_spin_percentage: float = 0.7 

var current_offset: Vector2 = Vector2.ZERO
var _max_radius_limit: float = 0.025 # Será atualizado pelo pai

func update_limit(ball_radius: float) -> void:
	_max_radius_limit = ball_radius * max_spin_percentage

func process_input(relative: Vector2) -> void:
	current_offset.x += relative.x * sensitivity
	current_offset.y -= relative.y * sensitivity 
	
	if current_offset.length() > _max_radius_limit:
		current_offset = current_offset.normalized() * _max_radius_limit

func reset() -> void:
	current_offset = Vector2.ZERO
