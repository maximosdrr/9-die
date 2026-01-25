class_name MatchManager extends Node

signal match_started
signal turn_changed(new_owner_id)
signal match_over

var match_already_started = false
var turn_owner = null
var turn_metadata: Dictionary = {}
var turn_history: Array[Dictionary] = []
var turn_order: Array[String] = []
var current_turn = 0

func start_match(players: Array[String], metadata: Dictionary = {}):
	_server_distribute_start_match.rpc_id(
		MultiplayerPeer.TARGET_PEER_SERVER,
		players, 
		metadata
	)

func call_next_turn(metadata: Dictionary = {}):
	if not match_already_started: return
	
	turn_history.append(turn_metadata)
	
	current_turn += 1
	var next_player_index = current_turn % turn_order.size()
	turn_owner = turn_order[next_player_index]
	
	turn_metadata = metadata
	
	turn_changed.emit(turn_owner)

func end_match():
	match_already_started = false
	current_turn = 0
	turn_owner = null
	turn_metadata = {}
	turn_history.clear()
	turn_order.clear()
	match_over.emit()

#region Start Match Network
@rpc('any_peer', 'call_local', 'reliable')
func _server_distribute_start_match(players: Array[String], metadata: Dictionary = {}):
	if not multiplayer.is_server():
		return
		
	_send_client_start_match_request.rpc(players, metadata)

@rpc('authority', 'call_local', 'reliable')
func _send_client_start_match_request(players: Array[String], metadata: Dictionary = {}):
	_start_match(players, metadata)

func _start_match(players: Array[String], metadata: Dictionary = {}):
	if match_already_started: return
	
	turn_order.clear()
	turn_history.clear()

	turn_order.append_array(players)
	
	turn_owner = turn_order[0]
	turn_metadata = metadata
	turn_history.append(metadata)
	
	match_already_started = true
	match_started.emit()
	
#endregion
