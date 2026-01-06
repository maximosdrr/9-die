extends State

class_name PlayerAimingState

@export var player: Player

func _init() -> void:
	self.type = State.Type.AIMING
	
func enter(_metadata: Dictionary[Variant, Variant]):
	player.hide()
	player.set_physics_process(false)
	player.player_camera.current = false
	player.table.aim.camera.current = true
	player.table.aim.camera_pivot.rotation_target = player.table.ball_stop_position
	player.table.aim.poolstick.cue_ball = player.table.cue_ball
	
func exit(_metadata: Dictionary[Variant, Variant]):
	player.player_camera.current = true
	player.table.aim.camera.current = false

func process(_delta: float) -> void:
	if Input.is_action_just_pressed("aim"):
		state_machine.change_state(State.Type.IDLE, {})
		return
	
