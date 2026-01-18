class_name PlayerGameHandler extends Node

@export var context_slot: Node3D
@export var player: Player

var current_controller: PlayerGameController = null

func equip_game_controller(
	controller_scene: PackedScene, 
	table_game: TableGame
	):
	unequip_current_controller()
	
	var controller_instance = controller_scene.instantiate()
	controller_instance.name = "ActiveController"
	controller_instance.set_multiplayer_authority(player.name.to_int())
	
	current_controller = controller_instance
	assert(current_controller is PlayerGameController)
	
	current_controller.hide()
	
	if not is_multiplayer_authority():
		context_slot.hide()
	
	context_slot.add_child(current_controller)
	current_controller.setup(owner, table_game)

func unequip_current_controller():
	if not current_controller: return
	
	var current_controller_parent = current_controller.get_parent()
	
	if current_controller_parent:
		current_controller_parent.remove_child(current_controller)
	
	current_controller.queue_free()
	current_controller = null
