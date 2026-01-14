class_name GoldenNineGameMode extends GameMode

func _init() -> void:
	self.type = Type.GOLDEN_NINE

func resolve_turn(context: Object) -> TurnActions:
	var balls_scored = context.get("balls_scored") as Dictionary
	var first_ball_touched = context.get("first_ball_touched") as Ball
	var balls_off_table = context.get("balls_off_table")
	var target_ball = context.get("target_ball") as Ball

	if balls_scored.has(0):
		return TurnActions.CALL_FOUL_WITH_ACTION 
	
	if first_ball_touched == null:
		return TurnActions.CALL_NEXT_TURN
		
	if balls_off_table and balls_off_table.size() > 0:
		return TurnActions.CALL_NEXT_TURN

	if target_ball.index != first_ball_touched.index:
		return TurnActions.CALL_NEXT_TURN

	if balls_scored.has(9):
		return TurnActions.END_GAME_PLAYER_WIN
	
	
	if balls_scored.size() > 0:
		return TurnActions.EXTEND_TURN
	
	return TurnActions.CALL_NEXT_TURN
