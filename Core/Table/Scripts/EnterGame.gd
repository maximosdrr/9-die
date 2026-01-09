class_name EnterTableGame extends Node

@export var table: Table

func _on_player_entered(body: Node3D) -> void:
	if body is Player:
		var player = body as Player
		player.game_handler.equip_game_controller(
			table.game_controller_scene,
			table.current_table_game
		)
