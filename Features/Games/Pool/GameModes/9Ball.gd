class_name NineBallGameMode extends GameMode

func _init() -> void:
	self.type = Type.NINE_BALL

func resolve_turn(context: Dictionary):
	var balls_scored = context.get("balls_scored") as Dictionary[int, Ball]
	#var first_ball_touched = context.get("first_ball_touched") as Ball
	var balls_in_game = context.get("balls_in_game") as Dictionary[int, Ball]
	var balls_off_table = context.get("balls_off_table") as Array[Ball]
	
	var cue_ball_scored = balls_scored.get(0)
	
	if cue_ball_scored:
		return TurnActions.CALL_NEXT_TURN
	
	var target_ball = _get_target_ball(balls_in_game)
	
	#if target_ball.index != first_ball_touched.index:
		#return TurnActions.CALL_NEXT_TURN
	
	if balls_off_table.size() > 0:
		return TurnActions.CALL_NEXT_TURN
	
	var scored_nine_ball = balls_scored.get(9) as Ball
	
	if scored_nine_ball != null:
		if target_ball.index != scored_nine_ball.index:
			return TurnActions.END_GAME_FATAL_FOUL
		else:
			return TurnActions.END_GAME_PLAYER_WIN
	
	if balls_scored.keys().size() > 0:
		return TurnActions.EXTEND_TURN
	
	return TurnActions.CALL_NEXT_TURN
	
func _get_target_ball(balls_in_game: Dictionary) -> Ball:
	var target_index = balls_in_game.keys().min()
	return balls_in_game[target_index]
