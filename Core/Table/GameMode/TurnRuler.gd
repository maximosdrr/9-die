class_name TurnRuler extends Node

enum Actions {
	CALL_FOUL,
	CALL_CUE_BALL_REPLACEMENT,
	CALL_GOLDEN_BALL_REPLACEMENT,
	CALL_NEXT_TURN,
	EXTEND_TURN,
	END_GAME_FATAL_FOUL,
	END_GAME_PLAYER_WIN,
}

enum Type {
	GOLDEN_NINE,
}

var type: Type

func rule(context: Dictionary):
	pass
