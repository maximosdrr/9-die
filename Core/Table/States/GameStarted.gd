class_name GameStarted extends State

@export var table: Table

func _init() -> void:
	self.type = State.Type.GAME_STARTED

func enter(metadata: Dictionary[Variant, Variant]):
	table.players_on_match = []
	var players_ids = metadata.get('players_ids')
	assert(players_ids != null)

	for player_id in players_ids:
		var player: Player = _get_player(player_id)
		assert(player != null)
		
		player.game_handler.equip_game_controller(
				table.game_controller_scene,
				table.current_table_game
		)
		
		table.players_on_match.append(player.name)
		
	table.match_started.emit(table.players_on_match)

func _get_player(player_id: String):
	var players_container = get_tree().get_nodes_in_group("players_container")[0]
	assert(players_container != null)
	
	for node in players_container.get_children():
		if node is Player and node.name == player_id:
			return node
