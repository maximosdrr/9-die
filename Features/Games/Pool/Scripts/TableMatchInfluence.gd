class_name EnterPoolGame extends Node

const POOL_CONTROLLER = preload("uid://vr3ram3d1ioc")
@export var pool_game: PoolGame

func _on_table_match_influence_body_entered(body: Node3D) -> void:
	if body is Player:
		var player = body as Player
		
		player.game_handler.equip_game_controller(
			POOL_CONTROLLER, 
			pool_game
		)


func _on_table_match_influence_body_exited(body: Node3D) -> void:
	if body is Player:
		var player = body as Player
		player.game_handler.unequip_current_controller()
