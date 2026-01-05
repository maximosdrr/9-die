extends Node3D

class_name State
var state_machine: StateMachine
var type: Type

func process(delta: float) -> void:
	pass

func physics_process(delta: float) -> void:
	pass
	
func enter(metadata: Dictionary[Variant, Variant]):
	pass

func exit(metadata: Dictionary[Variant, Variant]):
	pass

enum Type {
	IDLE,
	AIMING
}
