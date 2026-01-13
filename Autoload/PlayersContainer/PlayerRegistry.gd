extends Node

var players_container: Node3D

func has_container() -> bool:
	return is_instance_valid(players_container)

func get_player_by_id(_id: String) -> Player:
	var player: Player = null

	for node in players_container.get_children():
		if not node is Player: continue
		if node.name != _id: continue
		player = node as Player
		return player
	
	push_error("Player not found")
	return null
