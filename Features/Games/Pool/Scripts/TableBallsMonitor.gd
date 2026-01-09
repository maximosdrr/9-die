#TODO remove this script
class_name BallSleepMonitor extends Node

@export var balls_holder: Node3D

signal on_balls_stop
signal on_balls_move

var balls: Array[Ball] = []

var _previous_moving_count: int = 0

func _ready() -> void:
	_collect_balls()
	_previous_moving_count = _get_moving_balls_count()

func _physics_process(_delta: float) -> void:
	var current_moving_count := _get_moving_balls_count()
	
	if _previous_moving_count == 0 and current_moving_count > 0:
		on_balls_move.emit()
	
	elif _previous_moving_count > 0 and current_moving_count == 0:
		on_balls_stop.emit()
	
	_previous_moving_count = current_moving_count

func _collect_balls() -> void:
	balls.clear()
	for node in balls_holder.get_children():
		if node is Ball:
			balls.append(node)

func _get_moving_balls_count() -> int:
	return balls.filter(func(ball): return ball.state_machine.current.type == State.Type.MOVING).size()

func get_moving_balls_count() -> int:
	return _get_moving_balls_count()

func balls_are_stopped():
	return _previous_moving_count == 0
