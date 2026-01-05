extends State

class_name PlayerAimingState

@export var player: Player
var player_original_parent: Node3D = null

func _init() -> void:
	self.type = State.Type.AIMING
	
func _ready() -> void:
	player_original_parent = player.get_parent()

func enter(_metadata: Dictionary[Variant, Variant]):
	var table = player.table
	
	player.reparent(table.poolstick_respawn_marker)
	player.position = Vector3(0, 0, 0.5)
	player.set_physics_process(false)
	player.camera_pivot.rotation_x_enabled = false
	player.camera_pivot.rotation_target = table.poolstick_respawn_marker

func exit(_metadata: Dictionary[Variant, Variant]):
	player.reparent(player_original_parent)
	player.position = Vector3(0, 0, 2)
	player.set_physics_process(true)
	player.camera_pivot.rotation_x_enabled = true
	player.camera_pivot.rotation_target = null
	

func process(_delta: float) -> void:
	if Input.is_action_just_pressed("aim"):
		state_machine.change_state(State.Type.IDLE, {})
		return
