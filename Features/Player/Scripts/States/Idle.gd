extends State

class_name PlayerIdleState

@export var player: Player
	
func _init() -> void:
	self.type = State.Type.IDLE
	
func process(_delta: float) -> void:
	if Input.is_action_just_pressed("aim"):
		state_machine.change_state(State.Type.AIMING, {})
