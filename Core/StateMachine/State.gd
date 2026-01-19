extends Node3D

class_name State
var state_machine: StateMachine
var type: Type
var parent: Node3D

func process(delta: float) -> void:
	pass

func physics_process(delta: float) -> void:
	pass
	
func enter(metadata: Dictionary[Variant, Variant]):
	pass

func exit(metadata: Dictionary[Variant, Variant]):
	pass

func setup(parent_node: Node3D):
	pass

func handle_input(event: InputEvent) -> void:
	pass

enum Type {
	IDLE,
	AIMING,
	MOVING,
	WAITING_GAME_START,
	GAME_STARTING,
	GAME_STARTED,
	GAME_FINISHED,
	CUE_CHARGING,
	CUE_RECOVER,
	CUE_LOCKED,
}
