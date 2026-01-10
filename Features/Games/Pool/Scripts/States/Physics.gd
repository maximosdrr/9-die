class_name PoolRoundStatePhysics extends State

func _init():
	type = Type.PHYSICS

func process(_delta: float) -> void:
	# Poll the manager to see if balls stopped moving
	if parent.are_balls_stopped():
		state_machine.change_state(Type.EVALUATION, {})
