extends State

class_name PlayerIdleState

func _init() -> void:
	self.type = State.Type.IDLE

func process(delta: float) -> void:
	if Input.is_action_just_pressed("aim"):
		state_machine.change_state(State.Type.AIMING, {})
