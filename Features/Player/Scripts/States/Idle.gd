extends State

class_name PlayerIdleState

@export var player: Player

var table: Table = null
var poolstick: Poolstick = null

func _ready() -> void:
	table = player.table
	poolstick = player.poolstick
	
func _init() -> void:
	self.type = State.Type.IDLE
	
func enter(_metadata: Dictionary[Variant, Variant]):
	player.player_camera.current = true
	poolstick.poolstick_camera.current = false
	poolstick.set_camera_target(null)
	
	#Change poolstick parent
	table.poolstick_respawn_marker.remove_child(poolstick)
	player.add_child(poolstick)
	
	#Move poolstick to the right position
	poolstick.position = Vector3(0.31, -0.132, 0.572)
	poolstick.rotation_degrees = Vector3(-73.6, 0, 0)

func process(_delta: float) -> void:
	if Input.is_action_just_pressed("aim"):
		state_machine.change_state(State.Type.AIMING, {})
