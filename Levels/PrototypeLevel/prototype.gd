class_name GameLevel extends Node3D

# --- Configuration ---
# Update this path to your actual player scene
const PLAYER_SCENE_PATH = "uid://dq4mwkkjaq18o"

# --- Nodes ---
@onready var players_container: Node3D = $PlayersContainer
@onready var multiplayer_spawner: MultiplayerSpawner = $MultiplayerSpawner
# Create Node3Ds named "SpawnPoint1", "SpawnPoint2", etc. in your scene
@onready var spawn_points: Array[Node] = $SpawnPoints.get_children()


func _ready() -> void:
	# 1. Configuration must happen immediately on both Client and Host
	# The spawner needs to know *how* to build a node when the server says "spawn".
	multiplayer_spawner.spawn_function = _initialize_player_node
	
	# Point the spawner to the container so players appear inside the "Players" node
	multiplayer_spawner.spawn_path = players_container.get_path()

# 2. Called by your LobbyMenu when the game starts
func start_match() -> void:
	if multiplayer.is_server():
		print("Host: Starting match logic...")
		
		# Connect to NetworkManager signals to handle late-joiners or disconnections
		if not NetworkManager.player_connected.is_connected(_on_player_connected):
			NetworkManager.player_connected.connect(_on_player_connected)
		
		if not NetworkManager.player_disconnected.is_connected(_on_player_disconnected):
			NetworkManager.player_disconnected.connect(_on_player_disconnected)
		
		# Spawn the host immediately
		_spawn_player(1)

# --- Spawning Logic (Server Side Only) ---

func _on_player_connected(peer_id: int) -> void:
	print("Server: Spawning player %s" % peer_id)
	_spawn_player(peer_id)

func _on_player_disconnected(peer_id: int) -> void:
	print("Server: Despawning player %s" % peer_id)
	if players_container.has_node(str(peer_id)):
		var player = players_container.get_node(str(peer_id))
		PlayersManager.remove_player(player)
		player.queue_free()

func _spawn_player(peer_id: int) -> void:
	# Trigger the spawn. This sends a packet to all clients telling them:
	# "Run the spawn_function with this data."
	multiplayer_spawner.spawn(peer_id)

# --- Initialization Logic (Runs on ALL peers) ---

# This function is called automatically by the MultiplayerSpawner.
# It runs on the Host AND every Client to create the visual representation.
func _initialize_player_node(data) -> Node:
	var player_id = data
	var player_scene = load(PLAYER_SCENE_PATH)
	var player_instance = player_scene.instantiate()
	
	# Vital for MultiplayerSynchronizer to work
	player_instance.name = str(player_id)
	player_instance.set_multiplayer_authority(player_id)
	
	# Set Position
	var spawn_pos = Vector3(0, 5, 0) # Default fallback
	
	# Determine spawn point based on player count or ID (Basic logic)
	# This avoids players spawning inside each other
	var spawn_index = (players_container.get_child_count()) % spawn_points.size()
	if spawn_points.size() > 0:
		spawn_pos = spawn_points[spawn_index].global_position
		
	player_instance.global_position = spawn_pos
	PlayersManager.add_player(player_instance as Player)
	
	return player_instance
