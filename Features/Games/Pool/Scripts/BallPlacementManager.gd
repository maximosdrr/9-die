class_name BallPlacementManager extends Node

signal placement_finished

@export_category("Configuration")
@export var overhead_view_remote: RemoteTransform3D
@export var table_surface_y: float = 0.85

@export_group("Table Limits")
@export var play_area_width: float = 1.1
@export var play_area_length: float = 2.3
@export var ball_radius: float = 0.03

var _ball: Ball
var _is_placing := false
var _plane: Plane
var _previous_camera_remote: RemoteTransform3D
var _placement_token := 0

func _ready() -> void:
	_plane = Plane(Vector3.UP, table_surface_y)
	set_process_unhandled_input(false)

func start_placement(ball: Ball) -> void:
	if not is_instance_valid(ball): return
	if Global.camera == null: return

	_ball = ball
	_is_placing = true
	_placement_token += 1
	
	_set_ball_placement_state.rpc(_ball.get_path(), multiplayer.get_unique_id(), true, _ball.global_position)

	_enter_input_mode()
	_switch_to_overhead_camera()
	
	set_process_unhandled_input(true)

func _unhandled_input(event: InputEvent) -> void:
	if not _is_placing or not is_instance_valid(_ball): return
	
	if event is InputEventMouseMotion:
		_move_ball_to_mouse(event.position)
		return

	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_confirm_placement()
		return

func _move_ball_to_mouse(screen_position: Vector2) -> void:
	var cam := Global.camera
	var ray_origin := cam.project_ray_origin(screen_position)
	var ray_dir := cam.project_ray_normal(screen_position)

	var hit = _plane.intersects_ray(ray_origin, ray_dir)
	if hit == null: return

	var half_w := play_area_width * 0.5
	var half_l := play_area_length * 0.5
	var limit_x := half_w - ball_radius
	var limit_z := half_l - ball_radius

	var x = clamp(hit.x, -limit_x, limit_x)
	var z = clamp(hit.z, -limit_z, limit_z)

	_ball.global_position = Vector3(x, table_surface_y + ball_radius, z)

func _confirm_placement() -> void:
	if not _is_placing: return

	_is_placing = false
	set_process_unhandled_input(false)
	_exit_input_mode()

	if is_instance_valid(_ball):
		_set_ball_placement_state.rpc(_ball.get_path(), 1, false, _ball.global_position)

	_restore_camera()
	_ball = null
	placement_finished.emit()

func _enter_input_mode() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

func _exit_input_mode() -> void:
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

func _switch_to_overhead_camera() -> void:
	if Global.camera and overhead_view_remote:
		_previous_camera_remote = Global.camera.current_remote
		Global.camera.transition_to(overhead_view_remote)

func _restore_camera() -> void:
	if Global.camera and _previous_camera_remote:
		Global.camera.transition_to(_previous_camera_remote)
	_previous_camera_remote = null

@rpc("any_peer", "call_local", "reliable")
func _set_ball_placement_state(ball_path: NodePath, new_authority: int, is_frozen: bool, final_pos: Vector3):
	var ball_node = get_node_or_null(ball_path)
	if not ball_node: return
	
	ball_node.global_position = final_pos
	
	ball_node.set_multiplayer_authority(new_authority)
	if ball_node.has_method("get_multiplayer_synchronizer"):
		ball_node.get_multiplayer_synchronizer().set_multiplayer_authority(new_authority)
	
	ball_node.freeze = is_frozen
	
	ball_node.linear_velocity = Vector3.ZERO
	ball_node.angular_velocity = Vector3.ZERO
	
	if not is_frozen:
		ball_node.can_sleep = false 
		ball_node.sleeping = false
		
		if ball_node.is_multiplayer_authority():
			ball_node.apply_central_impulse(Vector3.DOWN * 0.05)
			
			get_tree().create_timer(0.2, false).timeout.connect(func():
				if is_instance_valid(ball_node):
					ball_node.can_sleep = true
			)
