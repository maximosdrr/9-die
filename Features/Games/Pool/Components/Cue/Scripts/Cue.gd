class_name Cue extends Node3D

@export_group("References")
@export var stroke_system: CueStrokeSystem 
@export var spin_system: CueSpinSystem
@export var input_system: CueInputSystem
@export var stroke_network_bridge: CueNetworkStrokeBridge

@export_group("Power Config")
@export var max_speed_reference: float = 12.0
@export var force_multiplier: float = 1
@export var visual_gap: float = 0.01
@export var post_shot_cooldown: float = 0.25


signal strike_executed

var ball_radius_offset: float = 0.04
var is_charging: bool = false
var is_locked: bool = false
var strike_is_locked: bool = true
var pool_game: PoolGame
var cue_ball: Ball
var camera_pivot: AimCameraPivot

func setup(_pool_game: PoolGame, _camera_pivot: AimCameraPivot):
	cue_ball = _pool_game.cue_ball
	pool_game = _pool_game
	camera_pivot = _camera_pivot
	input_system.setup(self)
	stroke_network_bridge.setup(self)
	if not pool_game.match_started.is_connected(_on_match_start):
		pool_game.match_started.connect(_on_match_start)
	
	if not pool_game.turn_changed.is_connected(_on_turn_change):
		pool_game.turn_changed.connect(_on_turn_change)
	
	if not pool_game.turn_extended.is_connected(_on_turn_extendes):
		pool_game.turn_extended.connect(_on_turn_extendes)

func _ready() -> void:
	_update_system_limits()
	position.z = ball_radius_offset

func _on_match_start(_players_ids: Array, first_turn_player: String):
	if int(first_turn_player) == multiplayer.get_unique_id():
		strike_is_locked = false

func _on_turn_change(next_player_name: String, _context):
	if int(next_player_name) == multiplayer.get_unique_id():
		strike_is_locked = false

func _on_turn_extendes():
	strike_is_locked = false

func start_charging() -> void:
	if is_charging or is_locked or strike_is_locked: return
	is_charging = true
	stroke_system.start_charging(position.z)

func cancel_charging() -> void:
	if is_locked: return
	is_charging = false
	stroke_system.stop()
	_animate_reset_position()

func process_stroke_input(amount: float) -> void:
	if is_locked: return
	stroke_system.process_input(amount)

func process_spin_input(relative: Vector2) -> void:
	if is_charging or is_locked: return
	spin_system.process_input(relative)

func _process(_delta: float) -> void:
	if not is_multiplayer_authority():
		return
	# Atualiza Spin visualmente
	position.x = spin_system.current_offset.x
	position.y = spin_system.current_offset.y

	if is_locked: return

	if not is_charging:
		if position.z != ball_radius_offset: position.z = ball_radius_offset
		return

	# Lógica da Tacada
	var desired_z = stroke_system.current_draw
	desired_z = max(desired_z, ball_radius_offset)
	position.z = desired_z

	var avg_velocity = stroke_system.get_average_velocity()
	
	# Detecção da batida
	if desired_z <= (ball_radius_offset + 0.001) and avg_velocity < -0.1:
		_execute_strike(abs(avg_velocity))

func _execute_strike(impact_speed: float) -> void:
	if not cue_ball: return
	if strike_is_locked: return
	# 1. Ativa o BLOQUEIO imediatamente
	is_locked = true 
	is_charging = false
	strike_is_locked = true
	
	# Cálculo de força
	var raw_power = clamp(impact_speed / max_speed_reference, 0.0, 1.0)
	var final_force = pow(raw_power, 2.0) * force_multiplier
	
	print("Strike! Force: %.2f" % final_force)
	
	# Física da bola
	var dir = -global_transform.basis.z.normalized()
	dir.y = 0 
	var hit_offset = Vector3(spin_system.current_offset.x, spin_system.current_offset.y, 0.0)
	
	if final_force < 0.01:
		strike_is_locked = false
	else:
		if multiplayer.is_server():
			cue_ball.strike(dir, final_force, hit_offset)
			#Call effects here
		else:
			strike_executed.emit(dir, final_force, hit_offset)
	# Reset visual
	stroke_system.stop()
	_animate_reset_spin()
	
	# Anima o taco voltando suavemente
	var tween = create_tween()
	tween.tween_property(self, "position:z", ball_radius_offset, 0.1)
	
	# 2. Timer para liberar o bloqueio (Cooldown)
	get_tree().create_timer(post_shot_cooldown).timeout.connect(func():
		is_locked = false
		# Aqui você pode emitir um sinal se quiser avisar que o turno acabou
	)

func _update_system_limits() -> void:
	if cue_ball and is_inside_tree():
		ball_radius_offset = cue_ball.radius + visual_gap
		if spin_system:
			spin_system.update_limit(cue_ball.radius)

func _animate_reset_position() -> void:
	create_tween().tween_property(self, "position:z", ball_radius_offset, 0.2).set_trans(Tween.TRANS_SINE)

func _animate_reset_spin() -> void:
	create_tween().tween_property(spin_system, "current_offset", Vector2.ZERO, 0.5)
