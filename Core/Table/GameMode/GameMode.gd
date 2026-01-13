class_name GameMode extends Node

enum TurnActions {
	CALL_NEXT_TURN,
	EXTEND_TURN,
	END_GAME_FATAL_FOUL,
	END_GAME_PLAYER_WIN
}

enum Type {
	NINE_BALL,
}

var type: Type

func resolve_turn(context: Dictionary):
	pass
