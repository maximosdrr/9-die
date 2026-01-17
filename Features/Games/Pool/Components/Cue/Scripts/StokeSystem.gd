class_name CueStrokeSystem extends Node

@export_group("Stroke Settings")
@export var sensitivity: float = 0.005 
@export var max_draw_distance: float = 0.8
@export var buffer_size: int = 5

var current_draw: float = 0.0
var _accumulated_input: float = 0.0
var _velocity_buffer: Array[float] = []
var _previous_draw: float = 0.0
var _is_active: bool = false

func start_charging(start_offset: float) -> void:
	_is_active = true
	_accumulated_input = 0.0
	current_draw = start_offset
	_previous_draw = start_offset
	_velocity_buffer.clear()

func stop() -> void:
	_is_active = false

func process_input(relative: float) -> void:
	if _is_active:
		_accumulated_input += relative

func _process(delta: float) -> void:
	if not is_multiplayer_authority():
		return
	
	if not _is_active: return

	# 1. Aplica input ao draw atual
	if _accumulated_input != 0:
		current_draw += (_accumulated_input * sensitivity)
		# Nota: O clamp final do minimo (colisão) faremos no pai ou aqui se passarmos o offset
		current_draw = min(current_draw, max_draw_distance) 
		_accumulated_input = 0.0

	# 2. Calcula velocidade
	var instant_velocity = (current_draw - _previous_draw) / delta
	
	_velocity_buffer.push_front(instant_velocity)
	if _velocity_buffer.size() > buffer_size:
		_velocity_buffer.pop_back()
	
	_previous_draw = current_draw

func get_average_velocity() -> float:
	if _velocity_buffer.is_empty(): return 0.0
	var sum: float = 0.0
	for v in _velocity_buffer: sum += v
	return sum / _velocity_buffer.size()
