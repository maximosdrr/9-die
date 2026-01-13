class_name TableGame extends Node3D

var players_on_area: Dictionary[int, Player] = {}
var table_influence_area: Area3D
var players_container: PlayersContainer
var table: Table
var game_mode_handler: GameModeHandler
var turn_order: Array
var turn_owner: Player
var network_turn_syncronization: TableTurnNetworkBridge

signal turn_changed(next_player_name: String)
signal match_started(players_ids: Array)

func setup(_table: Table) -> void:
	table = _table
	assert(table.table_influence is Area3D)
	table_influence_area = table.table_influence
	_setup_game_modes()
	_setup_network_turn_syncronization(self)
	_connect_signals()
	
func _setup_network_turn_syncronization(_table_game: TableGame):
	# Assuming 'table' is your data object with the config
	if not table.enable_network_turn_syncronization:
		return

	network_turn_syncronization = TableTurnNetworkBridge.new()
	network_turn_syncronization.name = "TableTurnNetworkBridge"
	
	add_child(network_turn_syncronization)
	
	# 3. Now it's safe to run setup (and connect signals)
	network_turn_syncronization.setup(_table_game)

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

func setup_first_turn(players: Array):
	turn_order = players
	
	var first_turn_owner_id = turn_order[0]
	var player = PlayerRegistry.get_player_by_id(first_turn_owner_id)
	
	turn_owner = player
	print("Game started! First player is: ", turn_owner.name)
	match_started.emit(players)
	
func call_next_turn():
	var current_id = turn_owner.name
	var current_index = turn_order.find(current_id)
	
	if current_index == -1:
		return
		
	var next_index = (current_index + 1) % turn_order.size()
	var next_player_id = turn_order[next_index]
	
	apply_new_turn(next_player_id)
	
	print("Turn passed to: ", next_player_id)

# Novo método público e centralizador
func apply_new_turn(player_id: String) -> void:
	var next_player = PlayerRegistry.get_player_by_id(player_id)
	
	if not next_player:
		push_error("Tentativa de mudar turno para jogador inexistente: " + str(player_id))
		return

	turn_owner = next_player
	turn_changed.emit(player_id)
