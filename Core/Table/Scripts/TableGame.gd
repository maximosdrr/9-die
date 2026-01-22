class_name TableGame extends Node3D

var players_on_area: Dictionary[int, Player] = {}
var table_influence_area: Area3D
var players_container: PlayersContainer
var table: Table
var game_mode_handler: GameModeHandler
var turn_order: Array
var turn_owner: Player
var player: Player
var network_turn_syncronization: TableTurnNetworkBridge

signal turn_changed(next_player_name: String, context: Dictionary)
signal match_started(players_ids: Array, first_turn_player: String)
signal turn_extended()
signal match_over(winner: String, context: Dictionary)

func setup(_table: Table) -> void:
	table = _table
	assert(table.table_influence is Area3D)
	table_influence_area = table.table_influence
	_setup_network_turn_syncronization(self)
	_connect_signals()
	
func _setup_network_turn_syncronization(_table_game: TableGame):
	if not table.enable_network_turn_syncronization:
		return

	network_turn_syncronization = TableTurnNetworkBridge.new()
	network_turn_syncronization.name = "TableTurnNetworkBridge"
	
	add_child(network_turn_syncronization)
	
	network_turn_syncronization.setup(_table_game)

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

func setup_match(players: Array, first_turn_owner_id: String):
	pass
	
func call_next_turn(context: Dictionary):
	var current_id = turn_owner.name
	var current_index = turn_order.find(current_id)
	
	if current_index == -1:
		return
		
	var next_index = (current_index + 1) % turn_order.size()
	var next_player_id = turn_order[next_index]
	
	apply_new_turn(next_player_id, context)
	
	print("Turn passed to: ", next_player_id)

func apply_new_turn(player_id: String, context: Dictionary) -> void:
	var next_player = PlayerRegistry.get_player_by_id(player_id)
	
	if not next_player:
		push_error("Tentativa de mudar turno para jogador inexistente: " + str(player_id))
		return

	turn_owner = next_player
	
	if game_mode_handler:
		game_mode_handler.current_game_mode.turn_resolver.handle_new_turn_context()
	else:
		push_warning("Game mode handler is not configured on table: ", name)
	turn_changed.emit(player_id, context)

func call_extend_current_turn():
	apply_turn_extension()
	print("Turn extended for: ", turn_owner.name)

func apply_turn_extension():
	turn_extended.emit()
	if game_mode_handler:
		game_mode_handler.current_game_mode.turn_resolver.handle_turn_extension_context()
	else:
		push_warning("Game mode handler is not configured on table: ", name)

func call_match_over(winner: String, context: Dictionary):
	apply_match_over(winner, context)

func apply_match_over(winner: String, context: Dictionary):
	table.state_machine.change_state(StatesRef.GAME_WAITING_START, {
		"is_restart": true
	})
	match_over.emit(winner, context)
