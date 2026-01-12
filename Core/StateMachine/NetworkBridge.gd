class_name StateMachineNetworkBridge extends Node

var state_machine: StateMachine

func setup(_state_machine: StateMachine) -> void:
	state_machine = _state_machine
	
	if multiplayer.is_server():
		multiplayer.peer_connected.connect(_on_client_connect)
		

#region Server
func _on_client_connect(peer_id: int):
	if not multiplayer.is_server():
		return
	
	#CLIENT SETUP
	rpc_id(peer_id, "_receive_state_from_server", state_machine.current.type, state_machine.current_metadata)
	rpc_id(peer_id, "_connect_client_signals")

@rpc("any_peer", "call_remote", "reliable")
func _receive_state_from_client(new_state, metadata):
	state_machine.change_state(new_state, metadata)

#endregion Server

#region Client
@rpc("authority", "call_remote", "reliable")
func _receive_state_from_server(new_state: State.Type, metadata):
	state_machine.change_state(new_state, metadata)

@rpc("authority", "call_remote", "reliable")
func _connect_client_signals():
	state_machine.state_changed.connect(_on_client_change_state)

func _on_client_change_state(new_state: State.Type, metadata: Dictionary[Variant, Variant]):
	rpc_id(1, "_receive_state_from_client", new_state, metadata)
#endregion Client
