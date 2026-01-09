class_name ExitTableGame extends Node

func _on_player_exited(body: Node3D) -> void:
	if body is Player:
		var player = body as Player
		player.game_handler.unequip_current_controller()
