class_name BallSoundEffects extends Node

@export_group("Audio Players")
@export var ball_rolling_sfx: AudioStreamPlayer3D
@export var ball_colliding_sfx: AudioStreamPlayer3D

@export_group("References")
@export var ball: Ball

@export_group("Settings")
@export var max_impact_speed: float = 10.0 
@export var min_rolling_db: float = -30.0
@export var max_simultaneous_collisions: int = 4
@export var min_rolling_speed: float = 0.35
@export var max_collision_delay: float = 0.02 

var _active_collision_sounds: int = 0

func _ready() -> void:
	if ball:
		if not ball.ball_contacted.is_connected(_on_hit_other_ball):
			ball.ball_contacted.connect(_on_hit_other_ball)

func _on_hit_other_ball(other_ball: Ball):
	var impact_speed = other_ball.linear_velocity.length()
	var intensity = clamp(impact_speed / max_impact_speed, 0.0, 1.0)
	
	_spawn_collision_sound(intensity)

func _spawn_collision_sound(intensity: float) -> void:
	if _active_collision_sounds >= max_simultaneous_collisions:
		return
	
	_active_collision_sounds += 1
	
	var new_sfx = AudioStreamPlayer3D.new()
	new_sfx.stream = ball_colliding_sfx.stream
	new_sfx.unit_size = ball_colliding_sfx.unit_size
	new_sfx.max_db = ball_colliding_sfx.max_db
	
	new_sfx.volume_db = linear_to_db(intensity)
	new_sfx.pitch_scale = randf_range(0.9, 1.1) 
	
	add_child(new_sfx)
	new_sfx.global_position = ball.global_position
	
	var delay = randf_range(0.0, max_collision_delay)
	
	if delay > 0.0:
		await get_tree().create_timer(delay).timeout
	
	if is_instance_valid(new_sfx):
		new_sfx.play()
		new_sfx.finished.connect(func():
			_active_collision_sounds -= 1
			new_sfx.queue_free()
		)
	else:
		_active_collision_sounds -= 1
