class_name PoolGameTable extends StaticBody3D

@onready var score_monitor: PoolScoreMonitor = $ScoreMonitor
@onready var ball_off_monitor: Area3D = $BallOffMonitor
@onready var ball_pocketed: AudioStreamPlayer3D = $BallPocketed

var last_sound_time_msec: int = 0
const MIN_SOUND_INTERVAL: int = 50

func _on_pockets_detectors_ball_entered(body: Node3D) -> void:
	if body is Ball:
		if body.get_meta("in_pocket", false):
			return
			
		body.set_meta("in_pocket", true)
		
		body.angular_velocity = Vector3.ZERO
		body.linear_velocity = Vector3(0.1, 0, 0.1)
		
		_emit_ball_pocketed_sound()
		
func _emit_ball_pocketed_sound():
	var current_time = Time.get_ticks_msec()
	if current_time - last_sound_time_msec < MIN_SOUND_INTERVAL:
		return
	
	last_sound_time_msec = current_time
	ball_pocketed.play()
