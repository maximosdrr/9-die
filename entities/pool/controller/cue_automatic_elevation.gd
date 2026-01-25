class_name CueAutomaticElevation extends Node

@export var physical_margin: float = 0.08
@export var cue: Cue
@export var cue_handle_sensor: RayCast3D
@export var aim_pivot: Node3D

func _physics_process(_delta: float) -> void:
	_update_safe_angle_limit()

func _update_safe_angle_limit() -> void:
	if not cue or not cue_handle_sensor or not aim_pivot: 
		return
	
	var safe_limit = 0.0
	
	if cue_handle_sensor.is_colliding():
		var collision_point = cue_handle_sensor.get_collision_point()
		var diff_y = (collision_point.y + physical_margin) - aim_pivot.global_position.y
		
		if diff_y > 0:
			var pivot_pos_2d = Vector2(aim_pivot.global_position.x, aim_pivot.global_position.z)
			var col_pos_2d = Vector2(collision_point.x, collision_point.z)
			var distance_to_obstacle = pivot_pos_2d.distance_to(col_pos_2d)
			
			distance_to_obstacle = max(distance_to_obstacle, 0.1)
			var angle_rad = atan2(diff_y, distance_to_obstacle)
			
			safe_limit = -abs(angle_rad)
	
	safe_limit = clamp(safe_limit, deg_to_rad(-45.0), 0.0)
	cue.min_safe_angle = safe_limit
