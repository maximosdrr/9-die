class_name GoldenNineMode extends RefCounted

var golden_nine_index = 9
var cue_ball_index = 0

func resolve_turn(data: PoolTurnData):
	var hit_nothing = data.first_ball_touched == null
	
	var cue_ball_scratched = (
		data.balls_dropped_off.has(cue_ball_index) or 
		data.pocketed_balls.has(cue_ball_index)
	)
	
	var hit_wrong_ball = data.first_ball_touched != data.target_ball
	var nine_ball_dropped = data.balls_dropped_off.has(golden_nine_index)
	var nine_ball_pocketed = data.pocketed_balls.has(golden_nine_index)

	if nine_ball_dropped:
		return PoolTurnCommands.CALL_MATCH_OVER_LOSER

	if nine_ball_pocketed and (cue_ball_scratched or hit_nothing or hit_wrong_ball):
		return PoolTurnCommands.CALL_MATCH_OVER_LOSER
		
	if nine_ball_pocketed:
		return PoolTurnCommands.CALL_MATCH_OVER_WINNER

	if cue_ball_scratched or hit_nothing or hit_wrong_ball:
		return PoolTurnCommands.CALL_BALL_REPLACEMENT

	if data.pocketed_balls.size() > 0:
		return PoolTurnCommands.CALL_EXTEND_TURN
		
	
	return PoolTurnCommands.CALL_NEXT_TURN
