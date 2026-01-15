class_name GameModeHandler extends Node

@export var current_game_mode: GameMode

var modes: Array[GameMode] = []

func setup(table_game: TableGame) -> void:
	for node in get_children():
		if node is GameMode:
			node.turn_resolver.setup(table_game)
			modes.append(node)
			

func switch(game_mode: GameMode):
	if game_mode != current_game_mode:
		current_game_mode = game_mode
