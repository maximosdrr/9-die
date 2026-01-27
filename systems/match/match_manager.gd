class_name MatchManager extends Node

signal match_started
signal turn_changed(new_owner_id)
signal match_over(metadata: Dictionary)

var match_already_started = false
var turn_owner = null
var turn_metadata: Dictionary = {}
var turn_history: Array[Dictionary] = []
var turn_order: Array[String] = []
var current_turn = 0
var players: Array[String] = []

func start_match(_players: Array[String], metadata: Dictionary = {}):
	_server_distribute_start_match_request.rpc_id(
		MultiplayerPeer.TARGET_PEER_SERVER,
		_players, 
		metadata
	)

func call_next_turn(metadata: Dictionary = {}):
	_server_distribute_next_turn_request.rpc_id(MultiplayerPeer.TARGET_PEER_SERVER, metadata)

func end_match(metadata: Dictionary = {}):
	_server_distribute_match_over_request.rpc_id(MultiplayerPeer.TARGET_PEER_SERVER, metadata)

#region Start Match Network
@rpc('any_peer', 'call_local', 'reliable')
func _server_distribute_start_match_request(_players: Array[String], metadata: Dictionary = {}):
	if not multiplayer.is_server():
		return
		
	_client_receive_start_match_request.rpc(_players, metadata)

@rpc('authority', 'call_local', 'reliable')
func _client_receive_start_match_request(_players: Array[String], metadata: Dictionary = {}):
	_start_match(_players, metadata)

func _start_match(_players: Array[String], metadata: Dictionary = {}):
	if match_already_started: return
	
	turn_order.clear()
	turn_history.clear()
	players.clear()

	turn_order.append_array(_players)
	players = _players
	
	turn_owner = turn_order[0]
	turn_metadata = metadata
	turn_history.append(metadata)
	
	match_already_started = true
	match_started.emit()
	
#endregion

#region Next turn Network
@rpc('any_peer', 'call_local', 'reliable')
func _server_distribute_next_turn_request(metadata: Dictionary):
	if not multiplayer.is_server():
		return
	
	_client_receive_next_turn_request.rpc(metadata)

@rpc('authority', 'call_local', 'reliable')
func _client_receive_next_turn_request(metadata: Dictionary):
	_call_next_turn(metadata)

func _call_next_turn(metadata: Dictionary):
	if not match_already_started: return
	
	turn_history.append(turn_metadata)
	
	current_turn += 1
	var next_player_index = current_turn % turn_order.size()
	turn_owner = turn_order[next_player_index]
	
	turn_metadata = metadata
	
	turn_changed.emit(turn_owner)
#endregion

#region End Match Network
@rpc("any_peer", "call_local", "reliable")
func _server_distribute_match_over_request(metadata: Dictionary):
	if not multiplayer.is_server():
		return
	
	_client_receive_match_over_request.rpc(metadata)

func _client_receive_match_over_request(metadata: Dictionary):
	_end_match(metadata)

func _end_match(metadata: Dictionary = {}):
	match_already_started = false
	current_turn = 0
	turn_owner = null
	turn_metadata = {}
	turn_history.clear()
	turn_order.clear()
	match_over.emit(metadata)
#endregion
