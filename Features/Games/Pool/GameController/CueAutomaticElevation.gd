class_name CueAutomaticElevantion extends Node


var extra_height_margin: float = 0.08
@export var cue: Cue
@export var cue_handle_sensor: RayCast3D
@export var aim_pivot: AimCameraPivot

func _physics_process(_delta: float) -> void:
	_update_cue_angle()


func _update_cue_angle() -> void:
	if not cue or not cue_handle_sensor: return
	
	var target_cue_pitch = 0.0
	
	if cue_handle_sensor.is_colliding():
		var collision_point = cue_handle_sensor.get_collision_point()
		
		var diff_y = (collision_point.y + extra_height_margin) - aim_pivot.global_position.y
		
		if diff_y > 0:
			var pivot_pos_2d = Vector2(aim_pivot.global_position.x, aim_pivot.global_position.z)
			var col_pos_2d = Vector2(collision_point.x, collision_point.z)
			var distance_to_obstacle = pivot_pos_2d.distance_to(col_pos_2d)
			
			distance_to_obstacle = max(distance_to_obstacle, 0.1)
			
			var angle_rad = atan2(diff_y, distance_to_obstacle)
			
			target_cue_pitch = -abs(angle_rad)
		else:
			target_cue_pitch = 0.0
	else:
		target_cue_pitch = 0.0
	
	target_cue_pitch = clamp(target_cue_pitch, deg_to_rad(-45.0), 0.0)
	cue.rotation.x = lerp(cue.rotation.x, target_cue_pitch, 0.2)
