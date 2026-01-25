class_name BallBounceAudio extends AudioStreamPlayer3D

@export_group("References")
@export var ball: Ball

@export_group("Bounce Logic")
@export var max_bounce_velocity: float = 4.0 
@export var min_bounce_velocity: float = 0.1
@export var max_simultaneous_sounds: int = 10

@export_group("Volume & Pitch")
@export var min_audible_db: float = -35.0
@export var pitch_min: float = 0.8
@export var pitch_max: float = 1.1

var _active_sounds: int = 0

func _ready() -> void:
	if not ball and get_parent() is Ball:
		ball = get_parent()
		
	if ball:
		if not ball.jump_landed.is_connected(_on_jump_landed):
			ball.jump_landed.connect(_on_jump_landed)

func _on_jump_landed() -> void:
	if not stream: return
	
	var impact_velocity_y = abs(ball.linear_velocity.y)
	
	if impact_velocity_y < min_bounce_velocity:
		return

	var raw_intensity = clamp(impact_velocity_y / max_bounce_velocity, 0.0, 1.0)
	var final_intensity = pow(raw_intensity, 2)
	
	_spawn_sound_clone(final_intensity)

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
	
	sfx.pitch_scale = lerp(pitch_min, pitch_max, intensity)
	sfx.pitch_scale += randf_range(-0.05, 0.05)
	
	add_child(sfx)
	sfx.global_position = ball.global_position
	
	if is_instance_valid(sfx):
		sfx.play()
		sfx.finished.connect(func():
			_active_sounds -= 1
			sfx.queue_free()
		)
	else:
		_active_sounds -= 1
