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
		
		player.game_handler.equip_game_controller(
				table.game_controller_scene,
				table.current_table_game
		)
		
		table.players_on_match.append(player.name)
		
	table.match_started.emit(table.players_on_match)
