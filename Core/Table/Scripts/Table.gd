@tool
class_name Table extends Node3D

@onready var table_game_handler: Node3D = $TableGameHandler
@onready var player_game_controller_handler: Node3D = $PlayerGameControllerHandler
@onready var table_influence: Area3D = $TableInfluence

@export_category("Scenes")
## This variable is used to player to instanciate the correct game controller
@export var game_controller_scene: PackedScene
@export var table_game_scene: PackedScene:
	set(value):
		table_game_scene = value
		if Engine.is_editor_hint():
			_rebuild_editor_preview()

var _editor_building := false
var current_table_game: TableGame

func _ready() -> void:
	if Engine.is_editor_hint():
		_rebuild_editor_preview()
		return

	_spawn_runtime_game()

func _spawn_runtime_game() -> void:
	if table_game_scene == null:
		push_error("Table scene is null!")
		return

	var table_game_instance := table_game_scene.instantiate()
	assert(table_game_instance is TableGame)
	(table_game_instance as TableGame).setup(table_influence)
	
	table_game_handler.add_child(table_game_instance)
	current_table_game = table_game_instance
	
	if  game_controller_scene == null:
		push_error("Player game controller is null!")
		return

func _rebuild_editor_preview() -> void:
	if table_game_handler == null:
		return

	if _editor_building:
		return
	_editor_building = true

	for c in table_game_handler.get_children():
		c.queue_free()

	if table_game_scene != null:
		var preview := table_game_scene.instantiate()
		preview.name = "TableGamePreview"
		table_game_handler.add_child(preview)
		
		if get_tree() and get_tree().edited_scene_root:
			preview.owner = get_tree().edited_scene_root

	_editor_building = false
