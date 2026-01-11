# network_manager.gd
extends Node

# --- Signals for UI and Game to listen to ---
signal lobby_list_received(lobbies: Array)
signal match_started
signal player_connected(peer_id: int)
signal player_disconnected(peer_id: int)
signal connection_failed(reason: String)

const MAX_PLAYERS = 4
const GAME_ID_KEY = "game"
const GAME_ID_VALUE = "MyGodotGame"
const USE_STEAM_NETWORK = false

var peer: SteamMultiplayerPeer
var current_lobby_id: int = 0

func _ready() -> void:
	# Hook into Steam signals here
	Steam.lobby_match_list.connect(_on_lobby_match_list)
	Steam.lobby_created.connect(_on_lobby_created)
	Steam.lobby_joined.connect(_on_lobby_joined)
	
	# Hook into Godot Multiplayer signals
	multiplayer.peer_connected.connect(func(id): player_connected.emit(id))
	multiplayer.peer_disconnected.connect(func(id): player_disconnected.emit(id))

# --- Public Actions (Called by UI) ---

func create_host_session() -> void:
	if USE_STEAM_NETWORK:
		# Existing Steam Logic
		Steam.createLobby(Steam.LOBBY_TYPE_PUBLIC, MAX_PLAYERS)
	else:
		# Local LAN Logic
		print("Starting LAN Host...")
		var enet = ENetMultiplayerPeer.new()
		enet.create_server(7777)
		multiplayer.multiplayer_peer = enet
		match_started.emit()

func join_session(lobby_id: int = 0) -> void:
	if USE_STEAM_NETWORK:
		# Existing Steam Logic
		Steam.joinLobby(lobby_id)
	else:
		# Local LAN Logic
		print("Joining LAN Host...")
		var enet = ENetMultiplayerPeer.new()
		enet.create_client("127.0.0.1", 7777)
		multiplayer.multiplayer_peer = enet
		match_started.emit()

func refresh_lobby_list() -> void:
	Steam.addRequestLobbyListDistanceFilter(Steam.LOBBY_DISTANCE_FILTER_WORLDWIDE)
	Steam.addRequestLobbyListStringFilter(GAME_ID_KEY, GAME_ID_VALUE, Steam.LOBBY_COMPARISON_EQUAL)
	Steam.requestLobbyList()

# --- Internal Logic ---

func _on_lobby_created(connect_result: int, lobby_id: int) -> void:
	if connect_result != Steam.Result.RESULT_OK:
		connection_failed.emit("Failed to create lobby.")
		return
		
	current_lobby_id = lobby_id
	
	# Set Lobby Data so others can find it
	Steam.setLobbyData(lobby_id, "name", Steam.getPersonaName() + "'s Game")
	Steam.setLobbyData(lobby_id, GAME_ID_KEY, GAME_ID_VALUE)
	
	# Initialize Host Peer
	peer = SteamMultiplayerPeer.new()
	peer.create_host(0)
	multiplayer.multiplayer_peer = peer
	
	print("Host Session Started. Lobby ID: ", lobby_id)
	match_started.emit()

func _on_lobby_joined(lobby_id: int, _permissions: int, _locked: bool, response: int) -> void:
	if response != Steam.ChatRoomEnterResponse.CHAT_ROOM_ENTER_RESPONSE_SUCCESS:
		connection_failed.emit("Failed to join lobby. Code: " + str(response))
		return

	current_lobby_id = lobby_id
	var host_id = Steam.getLobbyOwner(lobby_id)
	
	# Prevent joining your own lobby as a client
	if host_id == Steam.getSteamID(): 
		print("Host cannot rejoin in his own room")
		return 

	# Initialize Client Peer
	peer = SteamMultiplayerPeer.new()
	var error = peer.create_client(host_id, 0)
	
	if error == OK:
		multiplayer.multiplayer_peer = peer
		print("Client Session Started. Connected to Host: ", host_id)
		match_started.emit()
	else:
		connection_failed.emit("Peer creation failed: " + str(error))

func _on_lobby_match_list(lobbies: Array) -> void:
	lobby_list_received.emit(lobbies)
