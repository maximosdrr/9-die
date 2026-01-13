class_name TableTurnNetworkBridge extends Node

var table_game: TableGame

func setup(_table_game: TableGame) -> void:
	table_game = _table_game
	if not table_game.match_started.is_connected(_on_match_started_server_side):
		table_game.match_started.connect(_on_match_started_server_side)
		
	if not table_game.turn_changed.is_connected(_on_turn_changed_server_side):
		table_game.turn_changed.connect(_on_turn_changed_server_side)

func _on_match_started_server_side(players_ids: Array) -> void:
	if multiplayer.is_server():
		_rpc_sync_match_setup.rpc(players_ids)

func _on_turn_changed_server_side(next_player_id: String) -> void:
	if multiplayer.is_server():
		_rpc_sync_turn_update.rpc(next_player_id)

@rpc("authority", "call_remote", "reliable")
func _rpc_sync_match_setup(players_ids: Array) -> void:
	table_game.setup_first_turn(players_ids) 

@rpc("authority", "call_remote", "reliable")
func _rpc_sync_turn_update(next_player_id: String) -> void:
	table_game.apply_new_turn(next_player_id)
