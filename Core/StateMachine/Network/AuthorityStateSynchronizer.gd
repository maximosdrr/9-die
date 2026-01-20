class_name AuthorityStateSynchronizer extends Node

var state_machine: StateMachine
var is_incoming_network_change = false

func setup(_state_machine: StateMachine) -> void:
	state_machine = _state_machine
	
	if multiplayer.is_server():
		multiplayer.peer_connected.connect(_on_client_connect)
	
	state_machine.state_changed.connect(_on_local_state_change)

func _on_client_connect(peer_id: int):
	if multiplayer.is_server():
		rpc_id(peer_id, "_remote_sync_state", state_machine.current.type, state_machine.current_metadata)

func _on_local_state_change(type, metadata):
	if is_incoming_network_change:
		return
	
	if not is_multiplayer_authority():
		return
	
	if multiplayer.is_server():
		rpc("_remote_sync_state", type, metadata)
	else:
		rpc_id(1, "_remote_sync_state", type, metadata)

@rpc("any_peer", "call_remote", "reliable")
func _remote_sync_state(type, metadata):
	var sender_id = multiplayer.get_remote_sender_id()
	
	if multiplayer.is_server() and sender_id != 1:
		if sender_id != get_multiplayer_authority():
			push_warning("Peer %s attempted to change state without authority." % sender_id)
			return
	
	is_incoming_network_change = true
	state_machine.change_state(type, metadata)
	is_incoming_network_change = false
	
	if multiplayer.is_server():
		for peer_id in multiplayer.get_peers():
			if peer_id != sender_id:
				rpc_id(peer_id, "_remote_sync_state", type, metadata)
