class_name ENetNetworkProvider extends NetworkProvider

var enet: ENetMultiplayerPeer

func _ready() -> void:
	super._ready()

func _init() -> void:
	enet = ENetMultiplayerPeer.new()

func create_host(port: int = -1):
	if port == -1:
		var msg = 'Invalid port'
		push_error(msg)
		connection_failed.emit(msg)
	
	enet.create_server(port)
	multiplayer.multiplayer_peer = enet
	
	var peer_id = multiplayer.get_unique_id()
	var start_message = 'Host Created. Peer ID: %s' % [peer_id]
	
	print(start_message)
	
	lobby_created.emit(0, peer_id)
	player_connected.emit(peer_id)
	

func join_session(_lobby_id: int = 0,  host_address: String = '', port: int = -1):
	if host_address == '' or port == -1:
		var msg = 'Port or Client address invalid'
		push_error(msg)
		connection_failed.emit(msg)

	enet.create_client(host_address, port)
	multiplayer.multiplayer_peer = enet
	
	var peer_id = multiplayer.get_unique_id()
	lobby_session_joined.emit(0, peer_id, 1)
	
	var start_message = 'Joinned Session. Peer ID: %s' % [peer_id]
	print(start_message)
