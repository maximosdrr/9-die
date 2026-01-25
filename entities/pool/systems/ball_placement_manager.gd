class_name BallPlacementManager extends Node

signal placement_finished

const COLLISION_MARGIN = 0.001
const SAFETY_HEIGHT_MARGIN = 0.05
const WAKE_IMPULSE_STRENGTH = 0.05
const SLEEP_DELAY_SECONDS = 0.2

@export_category("Configuration")
@export var overhead_view_remote: RemoteTransform3D
@export var table_surface_y: float = 0.85

@export_group("Table Limits")
@export var play_area_width: float = 0.9
@export var play_area_length: float = 1.8
@export var ball_radius: float = 0.029

var _ball: Ball
var _other_balls: Array[Ball] = []
var _is_placing := false
var _table_plane: Plane
var _previous_camera_remote: RemoteTransform3D

func _ready() -> void:
	_table_plane = Plane(Vector3.UP, table_surface_y)
	set_process_unhandled_input(false)

func start_placement(ball_to_place: Ball, existing_balls: Array[Ball]) -> void:
	if not is_instance_valid(ball_to_place) or Global.camera == null:
		return

	_ball = ball_to_place
	_other_balls = existing_balls
	_is_placing = true
	
	_set_ball_placement_state.rpc(_ball.get_path(), multiplayer.get_unique_id(), true, _ball.global_position)
	_set_input_active(true)
	_switch_camera_mode(true)
	set_process_unhandled_input(true)

func _unhandled_input(event: InputEvent) -> void:
	if not _is_placing or not is_instance_valid(_ball):
		return
	
	if event is InputEventMouseMotion:
		_process_ball_movement(event.position)
	elif event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_try_confirm_placement()

func _process_ball_movement(screen_position: Vector2) -> void:
	var world_pos = _get_mouse_projection_on_table(screen_position)
	if world_pos == Vector3.INF:
		return

	world_pos.y = table_surface_y + ball_radius + SAFETY_HEIGHT_MARGIN
	
	world_pos = _apply_collision_sliding(world_pos)
	world_pos = _clamp_position_to_table(world_pos)
	
	_ball.global_position = world_pos

func _try_confirm_placement() -> void:
	if _check_for_overlaps():
		return
	
	_finish_placement()

func _finish_placement() -> void:
	_is_placing = false
	set_process_unhandled_input(false)
	_set_input_active(false)

	if is_instance_valid(_ball):
		_set_ball_placement_state.rpc(_ball.get_path(), 1, false, _ball.global_position)

	_switch_camera_mode(false)
	_ball = null
	placement_finished.emit()

func _get_mouse_projection_on_table(screen_position: Vector2) -> Vector3:
	var camera = Global.camera
	var ray_origin = camera.project_ray_origin(screen_position)
	var ray_dir = camera.project_ray_normal(screen_position)
	
	var intersection = _table_plane.intersects_ray(ray_origin, ray_dir)
	if intersection == null:
		return Vector3.INF
		
	return intersection

func _apply_collision_sliding(proposed_pos: Vector3) -> Vector3:
	var current_pos = proposed_pos
	var min_separation_dist = (ball_radius * 2.0) + COLLISION_MARGIN
	
	for other_ball in _other_balls:
		if not _is_valid_obstacle(other_ball):
			continue
		
		var obstacle_pos = other_ball.global_position
		obstacle_pos.y = current_pos.y
		
		var distance = current_pos.distance_to(obstacle_pos)
		
		if distance < min_separation_dist:
			var direction = (current_pos - obstacle_pos).normalized()
			if direction == Vector3.ZERO:
				direction = Vector3.RIGHT
			
			current_pos = obstacle_pos + (direction * min_separation_dist)
			
	return current_pos

func _clamp_position_to_table(pos: Vector3) -> Vector3:
	var limit_x = (play_area_width * 0.5) - ball_radius
	var limit_z = (play_area_length * 0.5) - ball_radius
	
	pos.x = clamp(pos.x, -limit_x, limit_x)
	pos.z = clamp(pos.z, -limit_z, limit_z)
	return pos

func _check_for_overlaps() -> bool:
	var min_dist = (ball_radius * 2.0) - COLLISION_MARGIN
	
	for other_ball in _other_balls:
		if not _is_valid_obstacle(other_ball):
			continue
			
		if _ball.global_position.distance_to(other_ball.global_position) < min_dist:
			return true
			
	return false

func _is_valid_obstacle(other_ball: Ball) -> bool:
	return is_instance_valid(other_ball) and other_ball != _ball

func _set_input_active(active: bool) -> void:
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE if active else Input.MOUSE_MODE_CAPTURED

func _switch_camera_mode(to_overhead: bool) -> void:
	if not Global.camera:
		return
		
	if to_overhead:
		if overhead_view_remote:
			_previous_camera_remote = Global.camera.current_remote
			Global.camera.transition_to(overhead_view_remote)
	else:
		if _previous_camera_remote:
			Global.camera.transition_to(_previous_camera_remote)
		_previous_camera_remote = null

@rpc("any_peer", "call_local", "reliable")
func _set_ball_placement_state(ball_path: NodePath, new_authority: int, is_frozen: bool, final_pos: Vector3) -> void:
	var ball_node = get_node_or_null(ball_path)
	if not ball_node:
		return
	
	ball_node.global_position = final_pos
	ball_node.set_multiplayer_authority(new_authority)
	
	if ball_node.has_method("get_multiplayer_synchronizer"):
		ball_node.get_multiplayer_synchronizer().set_multiplayer_authority(new_authority)
	
	ball_node.freeze = is_frozen
	ball_node.linear_velocity = Vector3.ZERO
	ball_node.angular_velocity = Vector3.ZERO
	
	if not is_frozen:
		_wake_up_ball(ball_node)

func _wake_up_ball(ball_node: Node) -> void:
	ball_node.can_sleep = false
	ball_node.sleeping = false
	
	if ball_node.is_multiplayer_authority():
		ball_node.apply_central_impulse(Vector3.DOWN * WAKE_IMPULSE_STRENGTH)
		
		get_tree().create_timer(SLEEP_DELAY_SECONDS, false).timeout.connect(func():
			if is_instance_valid(ball_node):
				ball_node.can_sleep = true
		)
