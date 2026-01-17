class_name GameStarted extends State

@export var table: Table

func _init() -> void:
	self.type = State.Type.GAME_STARTED

func enter(metadata: Dictionary[Variant, Variant]):
	table.players_on_match = []
	var players_ids = metadata.get('players_ids')
	assert(players_ids != null)

	for player_id in players_ids:
		var player: Player = PlayerRegistry.get_player_by_id(player_id)
		assert(player != null)
		table.players_on_match.append(player.name)
		
		if int(player.name) == multiplayer.get_unique_id():
			table.current_table_game.player = player
	
	table.current_table_game.turn_order = players_ids
	var first_turn_owner_id = players_ids[0]
	
	var _player = PlayerRegistry.get_player_by_id(first_turn_owner_id)
	table.current_table_game.turn_owner = _player
	
	if multiplayer.is_server():
		table.current_table_game.setup_match(players_ids, first_turn_owner_id)
	
