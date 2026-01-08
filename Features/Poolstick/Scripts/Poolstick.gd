class_name Poolstick extends Node3D

@export_group("References")
@export var cue_ball: Ball

@export_group("Stroke Settings")
@export var stroke_sensitivity: float = 0.005 
@export var max_draw_distance: float = 0.8
@export var ball_radius_offset: float = 0.04
@export var max_speed_reference: float = 15
@export var force_multiplier: float = 1.2

@export_group("Spin Settings")
@export var spin_sensitivity: float = 0.0005
@export var max_spin_offset: float = 0.025

var _is_charging: bool = false
var _is_adjusting_spin: bool = false 

var _previous_z: float = 0.0
var _stick_velocity: float = 0.0
var _accumulated_mouse_y: float = 0.0

# NOVO: Histórico para suavizar picos de velocidade
var _velocity_buffer: Array[float] = []
const BUFFER_SIZE: int = 5 # Média dos últimos 5 frames

var _spin_offset: Vector2 = Vector2.ZERO 

func set_cue_ball(ball: Ball):
	cue_ball = ball

func _ready() -> void:
	position.z = ball_radius_offset
	_previous_z = position.z

func _unhandled_input(event: InputEvent) -> void:
	# ... (Mantém sua lógica de input igual) ...
	# Apenas replique o que já tem no seu script aqui
	if event.is_action_pressed("spin_modifier"):
		_is_adjusting_spin = true
		Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	elif event.is_action_released("spin_modifier"):
		_is_adjusting_spin = false
	
	if _is_adjusting_spin and event is InputEventMouseMotion:
		_handle_spin_input(event.relative)
		get_viewport().set_input_as_handled()
		return 

	if event.is_action_pressed("stroke_mode"):
		_start_charging()
		get_viewport().set_input_as_handled()
	elif event.is_action_released("stroke_mode"):
		_cancel_charging()
		return

	if _is_charging and event is InputEventMouseMotion:
		_accumulated_mouse_y += event.relative.y
		get_viewport().set_input_as_handled()

func _process(delta: float) -> void:
	position.x = _spin_offset.x
	position.y = _spin_offset.y

	if not _is_charging:
		return
		
	_apply_movement_logic()
	
	var current_z = position.z
	
	# --- CÁLCULO DE VELOCIDADE COM BUFFER ---
	
	# 1. Calcula a velocidade instantânea deste frame
	var instant_velocity = (current_z - _previous_z) / delta
	
	# 2. Adiciona ao histórico
	_velocity_buffer.push_front(instant_velocity)
	if _velocity_buffer.size() > BUFFER_SIZE:
		_velocity_buffer.pop_back() # Remove o mais antigo
	
	# 3. Calcula a velocidade média (Suavizada)
	_stick_velocity = _calculate_average_velocity()
	
	# ----------------------------------------
	
	_previous_z = current_z
	
	# Verifica impacto usando a velocidade média
	if current_z <= (ball_radius_offset + 0.001) and _stick_velocity < -0.1:
		_execute_strike()

# NOVO: Função auxiliar para média
func _calculate_average_velocity() -> float:
	if _velocity_buffer.is_empty():
		return 0.0
	
	var sum: float = 0.0
	for v in _velocity_buffer:
		sum += v
	return sum / _velocity_buffer.size()

func _handle_spin_input(relative: Vector2) -> void:
	_spin_offset.x += relative.x * spin_sensitivity
	_spin_offset.y -= relative.y * spin_sensitivity 
	if _spin_offset.length() > max_spin_offset:
		_spin_offset = _spin_offset.normalized() * max_spin_offset

func _start_charging() -> void:
	_is_charging = true
	_accumulated_mouse_y = 0.0
	
	_previous_z = position.z
	_stick_velocity = 0.0
	
	# IMPORTANTE: Limpar o buffer ao começar para não pegar valores velhos
	_velocity_buffer.clear()

func _cancel_charging() -> void:
	_is_charging = false
	_reset_animation()

func _apply_movement_logic() -> void:
	if _accumulated_mouse_y == 0: return
	var target_z = position.z + (_accumulated_mouse_y * stroke_sensitivity)
	target_z = clamp(target_z, ball_radius_offset, max_draw_distance)
	position.z = target_z
	_accumulated_mouse_y = 0.0

func _execute_strike() -> void:
	if not cue_ball: return

	# Usa a velocidade média calculada no _process
	var impact_speed = abs(_stick_velocity)
	
	var raw_power = clamp(impact_speed / max_speed_reference, 0.0, 1.0)
	var curved_power = pow(raw_power, 2.0)
	var final_force = curved_power * force_multiplier
	
	print("Avg Speed: %.2f | Force: %.2f" % [impact_speed, final_force])
	
	var dir = -global_transform.basis.z.normalized()
	dir.y = 0 
	var hit_offset_3d = Vector3(_spin_offset.x, _spin_offset.y, 0.0)
	
	cue_ball.strike(dir, final_force, hit_offset_3d)
	
	_is_charging = false
	_reset_spin() 
	position.z = ball_radius_offset

func _reset_animation() -> void:
	var tween = create_tween()
	tween.tween_property(self, "position:z", ball_radius_offset, 0.2).set_trans(Tween.TRANS_SINE)

func _reset_spin() -> void:
	var tween = create_tween()
	tween.tween_property(self, "_spin_offset", Vector2.ZERO, 0.5)
