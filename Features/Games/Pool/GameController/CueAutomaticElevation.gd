class_name CueAutomaticElevation
extends Node

@export var physical_margin: float = 0.08

@export var cue: Cue
@export var cue_handle_sensor: RayCast3D
@export var aim_pivot: Node3D

func _physics_process(_delta: float) -> void:
	pass
	#_update_cue_angle()

func _update_cue_angle() -> void:
	if not cue or not cue_handle_sensor or not aim_pivot: return
	
	var target_cue_pitch = 0.0
	
	if cue_handle_sensor.is_colliding():
		var collision_point = cue_handle_sensor.get_collision_point()
		
		# Cálculo puramente físico: Altura do obstáculo + margem física
		var diff_y = (collision_point.y + physical_margin) - aim_pivot.global_position.y
		
		if diff_y > 0:
			var pivot_pos_2d = Vector2(aim_pivot.global_position.x, aim_pivot.global_position.z)
			var col_pos_2d = Vector2(collision_point.x, collision_point.z)
			var distance_to_obstacle = pivot_pos_2d.distance_to(col_pos_2d)
			
			distance_to_obstacle = max(distance_to_obstacle, 0.1)
			
			var angle_rad = atan2(diff_y, distance_to_obstacle)
			target_cue_pitch = -abs(angle_rad)
	
	# Limita fisicamente a 45 graus
	target_cue_pitch = clamp(target_cue_pitch, deg_to_rad(-45.0), 0.0)
	
	# Aplica rotação ao taco
	cue.rotation.x = lerp(cue.rotation.x, target_cue_pitch, 0.2)
