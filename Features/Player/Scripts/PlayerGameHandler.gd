class_name PlayerGameHandler extends Node

@export var context_slot: Node3D 

var current_controller: PlayerGameController = null

func equip_game_controller(controller_scene: PackedScene, table_game: TableGame):
	unequip_current_controller()
	
	current_controller = controller_scene.instantiate()
	assert(current_controller is PlayerGameController)
	
	current_controller.hide()
	
	context_slot.add_child(current_controller)
	current_controller.setup(owner, table_game)

func unequip_current_controller():
	if current_controller:
		current_controller.queue_free()
		current_controller = null
