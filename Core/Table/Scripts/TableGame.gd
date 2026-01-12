class_name TableGame extends Node3D

var players_on_area: Dictionary[int, Player] = {}
var table_influence_area: Area3D
var players_container: Node3D
var table: Table

func setup(_influence_area: Area3D, _table: Table) -> void:
	assert(_influence_area is Area3D)
	table_influence_area = _influence_area
	table = _table
	_connect_signals()
	
func _connect_signals() -> void:
	if not table_influence_area.body_entered.is_connected(_on_table_influence_body_entered):
		table_influence_area.body_entered.connect(_on_table_influence_body_entered)
	if not table_influence_area.body_exited.is_connected(_on_table_influence_body_exited):
		table_influence_area.body_exited.connect(_on_table_influence_body_exited)

func _on_table_influence_body_entered(body: Node3D) -> void:
	if players_on_area.has(body.get_instance_id()):
		return
	
	players_on_area.set(body.get_instance_id(), body)

func _on_table_influence_body_exited(body: Node3D) -> void:
	if not players_on_area.has(body.get_instance_id()):
		return
	
	players_on_area.erase(body.get_instance_id())
