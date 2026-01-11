extends Node

var players: Dictionary[int, Player] = {}

func add_player(player: Player):
	var player_id = player.name.to_int()
	
	if not players.has(player_id):
		var message = "Player %s is already in the player data set" % [player_id]
		push_error(message)
		return
	
	players.set(player_id, player)

func remove_player(player: Player):
	var player_id = player.name.to_int()
	
	if not players.has(player_id):
		var message = "Player %s is not in the player data set" % [player_id]
		push_error(message)
		return
	
	players.erase(player_id)
