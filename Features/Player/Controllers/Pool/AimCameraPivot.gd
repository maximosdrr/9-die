class_name AimCameraPivot extends Node3D

@onready var elevation_node: Node3D = $Elevation

@export_group("Camera Behavior")
@export var mouse_sensitivity: float = 0.0015
@export var distance_from_ball: float = 0.55
@export var height_offset: float = -0.2
@export var transition_duration: float = 0.1

@export_group("Rotation Limits")
@export var limit_ceiling_deg: float = -90.0 
@export var limit_floor_deg: float = 15.0 

var _rot_y: float = 0.0
var _rot_x: float = 0.0
var _camera_node: Node3D

var target: Ball
var pool_game: PoolGame
var _tween: Tween

func _notification(what: int) -> void:
	if what == NOTIFICATION_TRANSFORM_CHANGED:
		if not scale.is_equal_approx(Vector3.ONE):
			print("🚨 ESCALA ALTERADA PARA: ", scale)
			print("QUEM FEZ ISSO?")
			print_stack()

			
func setup(_pool_game: PoolGame, _pool_controller: PoolController):
	target = _pool_game.cue_ball
	pool_game = _pool_game
	_connect_signals()
	
	if target:
		await get_tree().create_timer(1.5).timeout
		global_position = target.global_position
		set_physics_process(true)

func _ready() -> void:
	set_as_top_level(true)
	scale = Vector3.ONE
	rotation = Vector3(0.0, rotation.y, 0.0)
	
	_initialize_positions()
	
	set_physics_process(false)
	set_notify_transform(true)

func _connect_signals() -> void:
	if not pool_game: return
	
	var events = [
		[pool_game.turn_changed, _on_turn_changed],
		[pool_game.turn_extended, _on_turn_extended],
		[pool_game.ball_placement_manager.placement_finished, _on_placement_finished]
	]
	
	for event in events:
		if not event[0].is_connected(event[1]):
			event[0].connect(event[1])

func _initialize_positions() -> void:
	_rot_y = rotation.y
	if elevation_node:
		#_rot_x = elevation_node.rotation.x
		var cam_child = elevation_node.get_child(0)
		if cam_child:
			_camera_node = cam_child
			cam_child.position.z = distance_from_ball
			cam_child.position.y = height_offset

func _physics_process(_delta: float) -> void:
	if not is_multiplayer_authority(): return
	
	if target:
		global_position = target.global_position

func _unhandled_input(event: InputEvent) -> void:
	if not is_multiplayer_authority(): return
	
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
			Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
	elif event.is_action_pressed("ui_cancel"):
		Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)

	if Input.get_mouse_mode() != Input.MOUSE_MODE_CAPTURED:
		return

	if event is InputEventMouseMotion:
		_apply_rotation(event.relative)

func _apply_rotation(relative_motion: Vector2) -> void:
	_rot_y -= relative_motion.x * mouse_sensitivity
	rotation.y = _rot_y
	
	if not elevation_node: return

	_rot_x -= relative_motion.y * mouse_sensitivity
	_rot_x = clamp(_rot_x, deg_to_rad(limit_ceiling_deg), deg_to_rad(limit_floor_deg))
	
	elevation_node.rotation.x = _rot_x


func _on_placement_finished():
	await get_tree().create_timer(1).timeout
	_move_smoothly_to_target()
	set_physics_process(true)

func _on_turn_extended():
	_move_smoothly_to_target()

func _on_turn_changed(_next_player_name: String, _context):
	_move_smoothly_to_target()

func _move_smoothly_to_target():
	if not target: return
	
	if pool_game and pool_game.cue_ball:
		target = pool_game.cue_ball

	if _tween: _tween.kill()
	_tween = create_tween()
	_tween.set_trans(Tween.TRANS_CUBIC)
	_tween.set_ease(Tween.EASE_OUT)
	_tween.tween_property(self, "global_position", target.global_position, transition_duration)
