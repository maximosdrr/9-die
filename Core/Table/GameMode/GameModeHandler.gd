class_name GameModeHandler extends Node

var modes: Array[GameMode] = []

@export var current_game_mode: GameMode

func _ready() -> void:
	for node in get_children():
		if node is GameMode:
			modes.append(node)

func resolve_turn(context: Object) -> GameMode.TurnActions:
	return current_game_mode.resolve_turn(context)
