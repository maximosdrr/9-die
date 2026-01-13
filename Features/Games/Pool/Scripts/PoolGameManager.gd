class_name PoolGameManager extends Node

signal turn_changed(player_id: int)

var pool_game: PoolGame

var turn_order: Array = []

func setup(_pool_game: PoolGame):
	pool_game = _pool_game
	pool_game.table.match_started.connect(_on_match_started)

func _on_match_started(players: Array[String]):
	print(multiplayer.get_unique_id(), " - ", "Players: ", players)
	return
