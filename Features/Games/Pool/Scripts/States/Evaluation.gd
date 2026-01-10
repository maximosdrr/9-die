class_name PoolRoundStateEvaluation extends State

func _init():
	type = Type.EVALUATION

func enter(_metadata: Dictionary) -> void:
	# 1. Check Rules (Did they scratch? Did they pot a ball?)
	var turn_result = _calculate_turn_result()
	
	# 2. Decide next step
	if turn_result == "FOUL" or turn_result == "MISS":
		parent.advance_player_index() # Next player
		print("Turn Pass")
	else:
		print("Keep Turn") # Index stays same
		
	# 3. Go back to aiming
	state_machine.change_state(Type.AIMING, {})

func _calculate_turn_result() -> String:
	# Implement your specific pool rules here
	return "MISS" # Placeholder
