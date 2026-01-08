class_name PlayerGameHandler extends Node

@export var context_slot: Node3D 

var current_controller: GameController = null

func equip_game_controller(controller_scene: PackedScene, ...args):
	unequip_current_controller()
	
	current_controller = controller_scene.instantiate()
	current_controller.hide()
	context_slot.add_child(current_controller)
	
	if current_controller.has_method("setup"):
		current_controller.setup(owner, args)

func unequip_current_controller():
	if current_controller:
		current_controller.queue_free()
		current_controller = null
