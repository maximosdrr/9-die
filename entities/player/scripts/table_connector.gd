class_name TableConnector extends Node

@export var table_detector: Area3D
@export var player: Player

func _ready() -> void:
	table_detector.area_entered.connect(_on_enter_table)
	table_detector.area_exited.connect(_on_exit_table)

func _on_enter_table(area: Area3D):
	if not is_multiplayer_authority():
		return

	var table: Table = area.get_parent()
	assert(table is Table)
	
	if not table.match_manager.players.has(player.name):
		return
	
	if not table.match_manager.match_already_started:
		return
	
	player.table = table

func _on_exit_table(area: Area3D):
	if not is_multiplayer_authority():
		return
		
	if player.table == null:
		return

	var table = area.get_parent()
	
	if table.get_instance_id() == player.table.get_instance_id():
		player.table = null
	
	
