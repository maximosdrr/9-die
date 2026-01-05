extends State

class_name PlayerAimingState

func _init() -> void:
	self.type = State.Type.AIMING

func process(_delta: float) -> void:
	if Input.is_action_just_pressed("aim"):
		state_machine.change_state(State.Type.IDLE, {})
