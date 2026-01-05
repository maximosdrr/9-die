extends State

class_name PlayerAimingState

@export var player: Player

func _init() -> void:
	self.type = State.Type.AIMING

func process(_delta: float) -> void:
	var poolstick = player.poolstick
	var table = player.table
	
	if Input.is_action_just_pressed("aim"):
		state_machine.change_state(State.Type.IDLE, {})
		return
	
	player.player_camera.current = false
	poolstick.poolstick_camera.current = true
	poolstick.set_camera_target(table.poolstick_respawn_marker)
	
	#Change poolstick parent
	table.poolstick_respawn_marker.add_child(poolstick)
	player.remove_child(poolstick)
	
	#Move poolstick to the right position
	poolstick.position = Vector3(0, 0.25, 1.6)
	poolstick.rotation_degrees = Vector3(-100, 0, 0)
	
