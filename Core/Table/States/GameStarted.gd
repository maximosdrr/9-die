class_name GameStarted extends State

@export var table: Table

func _init() -> void:
	self.type = State.Type.GAME_STARTED

func enter(metadata: Dictionary[Variant, Variant]):
	var players = metadata.get('players')
	assert(players != null)
	
	for player in players:
		player.game_handler.equip_game_controller(
				table.game_controller_scene,
				table.current_table_game
		)
