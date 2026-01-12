class_name NetworkProvider extends Node

const MAX_PLAYERS = 4
const GAME_ID_KEY = "game"
const GAME_ID_VALUE = "MyGodotGame"

signal lobby_created(lobby_id: int, host_peer_id: int)
signal lobby_session_joined(lobby_id: int, guest_peer_id: int, host_peer_id: int)
signal lobby_list_received(lobbies: Array)
signal connection_failed(error: String)
signal player_connected(id: int)
signal player_disconnected(id: int)

var peer: SteamMultiplayerPeer

func _ready() -> void:
	multiplayer.peer_connected.connect(func(id): player_connected.emit(id))
	multiplayer.peer_disconnected.connect(func(id): player_disconnected.emit(id))

func create_host(port: int = 7777):
	pass

func join_session(lobby_id: int, host_address: String = '', port: int = -1):
	pass

func refresh_lobby_list():
	pass
