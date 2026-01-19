extends Node3D

class_name State
var state_machine: StateMachine
var type: String
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
