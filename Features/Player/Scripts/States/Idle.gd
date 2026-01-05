extends State

class_name PlayerIdleState

@export var player: Player
	
func _init() -> void:
	self.type = State.Type.IDLE
	
func enter(_metadata: Dictionary[Variant, Variant]):
	var table = player.table
	var poolstick = player.poolstick
	player.player_camera.current = true
	poolstick.poolstick_camera.current = false
	poolstick.set_camera_target(null)
	
	if poolstick.get_parent() == table.poolstick_respawn_marker:
		table.poolstick_respawn_marker.remove_child(poolstick)
		player.add_child(poolstick)
		
		poolstick.position = Vector3(0.31, -0.132, 0.572)
		poolstick.rotation_degrees = Vector3(-73.6, 0, 0)


func process(_delta: float) -> void:
	if Input.is_action_just_pressed("aim"):
		state_machine.change_state(State.Type.AIMING, {})
