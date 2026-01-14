class_name GoldenNineGameMode extends GameMode

func _init() -> void:
	self.type = Type.GOLDEN_NINE

func resolve_turn(context: Object) -> TurnActions:
	var balls_scored = context.get("balls_scored") as Dictionary
	var first_ball_touched = context.get("first_ball_touched") as Ball
	var balls_in_game = context.get("balls_in_game") as Dictionary
	var balls_off_table = context.get("balls_off_table")
	

	if balls_scored.has(0):
		return TurnActions.CALL_FOUL_WITH_ACTION 
	
	if first_ball_touched == null:
		return TurnActions.CALL_NEXT_TURN
		
	if balls_off_table and balls_off_table.size() > 0:
		return TurnActions.CALL_NEXT_TURN

	# --- 3. CHECK LEGAL CONTACT (The Core 9-Ball Rule) ---
	
	var target_ball = _get_target_ball(balls_in_game)
	
	# If we hit anything other than the lowest ball first, it's a foul immediately.
	# Even if the 9 went in, this foul takes precedence.
	if target_ball.index != first_ball_touched.index:
		return TurnActions.CALL_NEXT_TURN

	# --- 4. CHECK WIN CONDITION ---
	
	# We only reach here if the contact was LEGAL.
	# If the 9-ball is scored on a LEGAL contact (Directly or via Combo), Player Wins.
	if balls_scored.has(9):
		return TurnActions.END_GAME_PLAYER_WIN
	
	# --- 5. CHECK CONTINUATION ---
	
	# Legal hit, no 9-ball, but we potted something else.
	if balls_scored.size() > 0:
		return TurnActions.EXTEND_TURN
	
	return TurnActions.CALL_NEXT_TURN

func _get_target_ball(balls_in_game: Dictionary) -> Ball:
	# Lowest index is always the target in 9-Ball
	var target_index = balls_in_game.keys().min()
	return balls_in_game[target_index]
