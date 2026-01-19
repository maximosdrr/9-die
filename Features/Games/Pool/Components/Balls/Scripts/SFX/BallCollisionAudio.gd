class_name BallCollisionAudio extends AudioStreamPlayer3D

@export_group("References")
@export var ball: Ball

@export_group("Collision Logic")
@export var max_impact_speed: float = 8.0 
@export var max_simultaneous_sounds: int = 5
@export var max_spawn_delay: float = 0.02 
@export var min_collision_speed: float = 0.05 

@export_group("Volume & Pitch")
@export var min_audible_db: float = -35.0
@export var pitch_min: float = 0.9
@export var pitch_max: float = 1.1

var _active_sounds: int = 0

func _ready() -> void:
	if not ball and get_parent() is Ball:
		ball = get_parent()

	if ball:
		if not ball.ball_contacted.is_connected(_on_ball_contacted):
			ball.ball_contacted.connect(_on_ball_contacted)

func _on_ball_contacted(other_ball: Ball) -> void:
	if not stream: return

	if ball.get_instance_id() < other_ball.get_instance_id():
		return 

	var relative_velocity = ball.linear_velocity - other_ball.linear_velocity
	var impact_speed = relative_velocity.length()
	
	if impact_speed < min_collision_speed:
		return
	
	var intensity = clamp(impact_speed / max_impact_speed, 0.0, 1.0)
	intensity = intensity * intensity 
	
	_spawn_sound_clone(intensity)

func _spawn_sound_clone(intensity: float) -> void:
	if _active_sounds >= max_simultaneous_sounds:
		return
	
	_active_sounds += 1
	
	var sfx = AudioStreamPlayer3D.new()
	
	sfx.stream = stream
	sfx.unit_size = unit_size
	sfx.max_db = max_db
	sfx.bus = bus
	sfx.attenuation_model = attenuation_model
	sfx.max_distance = max_distance
	
	var target_db = lerp(min_audible_db, volume_db, intensity)
	sfx.volume_db = target_db
	
	var dynamic_pitch = lerp(pitch_min, pitch_max, intensity)
	dynamic_pitch += randf_range(-0.02, 0.02)
	
	sfx.pitch_scale = dynamic_pitch
	
	add_child(sfx)
	sfx.global_position = ball.global_position
	
	var delay = randf_range(0.0, max_spawn_delay)
	if delay > 0.0:
		await get_tree().create_timer(delay).timeout
	
	if is_instance_valid(sfx):
		sfx.play()
		sfx.finished.connect(func():
			_active_sounds -= 1
			sfx.queue_free()
		)
	else:
		_active_sounds -= 1
