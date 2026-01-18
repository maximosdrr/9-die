class_name CueSfx extends Node

@export var strike_sfx: AudioStreamPlayer3D
@export var cue: Cue

@export_group("Audio Config")
@export var min_pitch: float = 0.8
@export var max_pitch: float = 1.1

@export var min_db: float = -40.0 
@export var max_db: float = 10.0

func emit_strike_sound(_dir: Vector3, final_force: float, _hit_offset: Vector3) -> void:
	strike_sfx.volume_db = lerp(min_db, max_db, final_force)
	strike_sfx.pitch_scale = lerp(min_pitch, max_pitch, final_force)
	strike_sfx.play()
