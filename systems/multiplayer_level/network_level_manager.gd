class_name LevelMultiplayerManager extends Node

const PLAYER_SCENE = preload("uid://dq4mwkkjaq18o")

@export var players_container: PlayersContainer
@export var multiplayer_spawner: MultiplayerSpawner
@export var spawn_points: Array[Marker3D]

func _ready() -> void:
	multiplayer_spawner.spawn_function = _initialize_player_node
	multiplayer_spawner.spawn_path = players_container.get_path()
	
	NetworkManager.network_provider.player_connected.connect(_on_player_connect)
	NetworkManager.network_provider.player_disconnected.connect(_on_player_disconnect)

func _on_player_connect(peer_id: int) -> void:
	if not is_multiplayer_authority():
		return
	multiplayer_spawner.spawn(peer_id)

func _on_player_disconnect(peer_id: int) -> void:
	print("Server - Player disconnected Peer ID: %s" % peer_id)

	if players_container.has_node(str(peer_id)):
		var player = players_container.get_node(str(peer_id))
		player.queue_free()

func _initialize_player_node(peer_id: int) -> Node:
	var player_instance: Player = PLAYER_SCENE.instantiate()
	
	player_instance.name = str(peer_id)
	player_instance.id = peer_id
	player_instance.set_multiplayer_authority(peer_id)
	
	if spawn_points.size() == 0:
		var msg = "There's no spawn points yet"
		push_error(msg)
		return
	
	var players_count = players_container.get_child_count()
	var spawn_index = players_count % spawn_points.size()
	
	var spawn_point = spawn_points[spawn_index]
	player_instance.global_transform = spawn_point.global_transform
	
	return player_instance
