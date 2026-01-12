class_name StateMachineNetworkBridge extends Node

@export var state_machine: StateMachine
var _suppress_broadcast := false

func _ready() -> void:
	state_machine.state_changed.connect(_on_state_change)

@rpc("any_peer", "call_remote", "reliable")
func request_change_state(new_state: State.Type, metadata: Dictionary) -> void:
	if not multiplayer.is_server():
		return

	_suppress_broadcast = true
	state_machine.change_state(new_state, metadata)
	_suppress_broadcast = false

	rpc("apply_state", new_state, metadata)

@rpc("authority", "call_remote", "reliable")
func apply_state(new_state: State.Type, metadata: Dictionary) -> void:
	_suppress_broadcast = true
	state_machine.change_state(new_state, metadata)
	_suppress_broadcast = false

func _on_state_change(new_state: State.Type, metadata: Dictionary) -> void:
	if _suppress_broadcast:
		return

	if multiplayer.is_server():
		rpc("apply_state", new_state, metadata)
	else:
		rpc_id(1, "request_change_state", new_state, metadata)
