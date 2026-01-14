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
	if not is_instance_valid(ball):
		return
	if Global.camera == null:
		return

	_ball = ball
	_is_placing = true
	_placement_token += 1

	_enter_input_mode()
	_switch_to_overhead_camera()

	_prepare_ball_for_placement()

	set_process_unhandled_input(true)
	print("Ball In Hand: Posicione a bola ", _ball.name)

func _unhandled_input(event: InputEvent) -> void:
	if not _is_placing or not is_instance_valid(_ball) or Global.camera == null:
		return
	
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
	if hit == null:
		return

	var half_w := play_area_width * 0.5
	var half_l := play_area_length * 0.5
	var limit_x := half_w - ball_radius
	var limit_z := half_l - ball_radius

	var x = clamp(hit.x, -limit_x, limit_x)
	var z = clamp(hit.z, -limit_z, limit_z)

	_ball.global_position = Vector3(x, table_surface_y + ball_radius, z)

func _confirm_placement() -> void:
	if not _is_placing:
		return

	_is_placing = false
	set_process_unhandled_input(false)

	_exit_input_mode()

	if is_instance_valid(_ball):
		_release_ball_safely(_ball, _placement_token)

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

func _prepare_ball_for_placement() -> void:
	_ball.freeze = true
	_ball.linear_velocity = Vector3.ZERO
	_ball.angular_velocity = Vector3.ZERO
	_ball.global_position.y = table_surface_y + ball_radius

func _release_ball_safely(ball: Ball, token: int) -> void:
	ball.freeze = false
	ball.sleeping = false
	ball.can_sleep = false

	# "assentar" levemente na mesa
	ball.linear_velocity = Vector3(0, -0.02, 0)
	ball.angular_velocity = Vector3.ZERO

	get_tree().create_timer(0.5).timeout.connect(func():
		if token != _placement_token:
			return
		if is_instance_valid(ball):
			ball.can_sleep = true
	)
