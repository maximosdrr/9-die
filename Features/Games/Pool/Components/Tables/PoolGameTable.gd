class_name PoolGameTable extends StaticBody3D

@onready var score_monitor: PoolScoreMonitor = $ScoreMonitor
@onready var ball_off_monitor: Area3D = $BallOffMonitor
@onready var ball_pocketed_audio: AudioStreamPlayer3D = $BallPocketed

var _last_sound_time_msec: int = 0
const MIN_SOUND_INTERVAL: int = 50

func _on_pockets_detectors_ball_entered(body: Node3D) -> void:
	var ball = body as Ball
	if not ball:
		return

	if ball.get_meta("in_pocket", false): 
		return
			
	ball.set_meta("in_pocket", true)
	
	call_deferred("_apply_pocket_physics", ball)
	_emit_ball_pocketed_sound()
	
	get_tree().create_timer(1.0).timeout.connect(func():
		if is_instance_valid(ball):
			ball.set_meta("in_pocket", false)
	)

func _apply_pocket_physics(ball: Ball) -> void:
	if is_instance_valid(ball):
		ball.angular_velocity = Vector3.ZERO
		ball.linear_velocity = Vector3(0.1, 0, 0.1)

func _emit_ball_pocketed_sound() -> void:
	var current_time = Time.get_ticks_msec()
	if current_time - _last_sound_time_msec < MIN_SOUND_INTERVAL:
		return
	
	_last_sound_time_msec = current_time
	ball_pocketed_audio.play()
