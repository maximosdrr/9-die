class_name PoolRoundStateAiming extends State

func _init():
	type = Type.AIMING

func enter(_metadata: Dictionary) -> void:
	var player = parent.get_current_player()
	print("Turn Start: ", player.name)
	player.enable_controls()
	
	# Connect to the player's shot signal
	if not player.shot_taken.is_connected(_on_shot_taken):
		player.shot_taken.connect(_on_shot_taken)

func exit(_metadata: Dictionary) -> void:
	parent.get_current_player().disable_controls()
	# Clean up signal to prevent double firing
	if parent.get_current_player().shot_taken.is_connected(_on_shot_taken):
		parent.get_current_player().shot_taken.disconnect(_on_shot_taken)

func _on_shot_taken(power: float):
	state_machine.change_state(State.Type.PHYSICS, { "shot_power": power })
