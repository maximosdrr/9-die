extends State

class_name PlayerAimingState

@export var player: Player
@export var poolstick_initial_distance = 0.3

var player_original_parent: Node3D = null

func _init() -> void:
	self.type = State.Type.AIMING
	
func _ready() -> void:
	player_original_parent = player.get_parent()

func enter(_metadata: Dictionary[Variant, Variant]):
	player.set_physics_process(false)
	player.collision_shape.disabled = true
	player.reparent(player.table.poolstick_respawn_marker)
	
	_adjust_positions(true)
	_setup_camera_pivot(true)
	
func exit(_metadata: Dictionary[Variant, Variant]):
	player.set_physics_process(true)
	player.collision_shape.disabled = false
	player.reparent(player_original_parent)
	
	_adjust_positions(false)
	_setup_camera_pivot(false)

func process(_delta: float) -> void:
	if Input.is_action_just_pressed("aim"):
		state_machine.change_state(State.Type.IDLE, {})
		return

func _adjust_positions(state_is_entering: bool):
	if state_is_entering:
		player.position = Vector3(0, 0, 1)
		player.poolstick.position = Vector3(0, -0.3, poolstick_initial_distance)
		player.poolstick.rotation_degrees = Vector3(-90, 0, 0)
	else:
		player.position = Vector3(0, 0, 2)
		player.poolstick.position = Vector3(-0.55, 0, 0.91)
		player.poolstick.rotation_degrees = Vector3(-85, 0, 0)

func _setup_camera_pivot(state_is_entering: bool):
	var table = player.table
	if state_is_entering:
		player.camera_pivot.rotation_x_enabled = false
		player.camera_pivot.rotation_target = table.poolstick_respawn_marker
	else:
		player.camera_pivot.rotation_x_enabled = true
		player.camera_pivot.rotation_target = null
