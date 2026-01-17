class_name GoldenNineTurnRuler extends TurnRuler

func _init() -> void:
	self.type = Type.GOLDEN_NINE

func rule(context: Dictionary) -> Actions:
	var balls_scored: Dictionary = context.get("balls_scored") 
	var first_ball_touched: Ball = context.get("first_ball_touched") 
	var balls_off_table: Array[Ball] = context.get("balls_off_table")
	var target_ball: Ball = context.get("target_ball")
	
	if balls_scored.has(0):
		if balls_scored.has(9):
			return Actions.END_GAME_FATAL_FOUL
		return Actions.CALL_CUE_BALL_REPLACEMENT 
	
	if first_ball_touched == null:
		return Actions.CALL_NEXT_TURN
	
	if balls_off_table and balls_off_table.size() > 0:
		var has_cue_ball = balls_off_table.any(
			func(ball: Ball): return ball.index == 0
		)
		
		if has_cue_ball: return Actions.CALL_CUE_BALL_REPLACEMENT
		
		return Actions.CALL_NEXT_TURN

	if target_ball.index != first_ball_touched.index:
		if balls_scored.has(9):
			return Actions.END_GAME_FATAL_FOUL

		return Actions.CALL_NEXT_TURN
	
	if balls_scored.has(9):
		return Actions.END_GAME_PLAYER_WIN
	
	if balls_scored.size() > 0:
		return Actions.EXTEND_TURN
	
	return Actions.CALL_NEXT_TURN
