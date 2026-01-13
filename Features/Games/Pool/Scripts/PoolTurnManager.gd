class_name PoolTurnManager extends Node

signal turn_changed(player_id: int)

var pool_game: PoolGame
var turn_order: Array = []
var turn_owner: Player
var match_started: bool = false

func setup(_pool_game: PoolGame):
	pool_game = _pool_game
	pool_game.table.match_started.connect(_on_match_started)

func _on_match_started(players: Array[String]):
	if match_started: return

	match_started = true

	turn_order = players
