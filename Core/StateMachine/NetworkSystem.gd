class_name StateMachineNetworkBridge extends Node

@export var state_machine: StateMachine

func _ready() -> void:
	state_machine.state_changed.connect(_on_state_change)

@rpc("any_peer", "call_remote", "reliable")
func change_state(new_state: State.Type, metadata: Dictionary[Variant, Variant]):
	if not multiplayer.is_server():
		return

	state_machine.change_state(new_state, metadata)

func _on_state_change(new_state: State.Type, metadata: Dictionary[Variant, Variant]):
	rpc_id(1, 'change_state', new_state, metadata)
