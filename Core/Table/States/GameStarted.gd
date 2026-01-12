class_name GameStarted extends State

@export var table: Table

func _init() -> void:
	self.type = State.Type.GAME_STARTED

func enter(metadata: Dictionary[Variant, Variant]):
	var players_ids = metadata.get('players_ids')
	assert(players_ids != null)
	
	var players_container = get_tree().get_nodes_in_group("players_container")[0]
	
	assert(players_container != null)
	for player_id in players_ids:
		var player = _get_player(players_container, player_id)
		assert(player != null)
		if multiplayer.is_server():
			player.game_handler.equip_game_controller(
					table.game_controller_scene,
					table.current_table_game
			)
		
func _get_player(players_container: Node3D, player_id: String):
	for node in players_container.get_children():
		if node is Player and node.name == player_id:
			return node
