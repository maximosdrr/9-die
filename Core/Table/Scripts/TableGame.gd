class_name TableGame extends Node3D

var players_on_area: Dictionary[int, Player] = {}
var table_influence_area: Area3D
var players_container: PlayersContainer
var table: Table
var game_mode_handler: GameModeHandler

func setup(_table: Table) -> void:
	table = _table
	assert(table.table_influence is Area3D)
	table_influence_area = table.table_influence
	_setup_game_modes()
	_connect_signals()

func _setup_game_modes():
	game_mode_handler = table.game_mode_handler
	
	for game_mode in table.game_modes:
		if not game_mode is GameMode: return
		
		game_mode.reparent(game_mode_handler)
		
		if game_mode.type == table.initial_game_mode:
			game_mode_handler.current_game_mode = game_mode 
	
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
