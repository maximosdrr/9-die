class_name SteamNetworkProvider extends NetworkProvider

func _ready() -> void:
	super._ready()
	Steam.lobby_match_list.connect(_on_lobby_match_list)
	Steam.lobby_created.connect(_on_lobby_created)
	Steam.lobby_joined.connect(_on_lobby_joined)

func create_host(_port: int = -1):
	Steam.createLobby(Steam.LOBBY_TYPE_PUBLIC, MAX_PLAYERS)

func join_session(lobby_id: int, _host_address: String = '', _port: int = -1):
	if lobby_id == null:
		var msg = "Lobby id is null"
		push_error(msg)
		connection_failed.emit(msg)
		
	Steam.joinLobby(lobby_id)

func refresh_lobby_list() -> void:
	Steam.addRequestLobbyListDistanceFilter(Steam.LOBBY_DISTANCE_FILTER_WORLDWIDE)
	Steam.addRequestLobbyListStringFilter(GAME_ID_KEY, GAME_ID_VALUE, Steam.LOBBY_COMPARISON_EQUAL)
	Steam.requestLobbyList()

func _on_lobby_match_list(lobbies: Array) -> void:
	lobby_list_received.emit(lobbies)

func _on_lobby_created(connect_result: int, lobby_id: int) -> void:
	if connect_result != Steam.Result.RESULT_OK:
		var msg = "Steam connection error: " + str(connect_result)
		push_error(msg)
		connection_failed.emit(msg)
		return
		
	Steam.setLobbyData(lobby_id, "name", Steam.getPersonaName() + "'s Game")
	Steam.setLobbyData(lobby_id, GAME_ID_KEY, GAME_ID_VALUE)
	
	peer = SteamMultiplayerPeer.new()
	peer.create_host(0)
	multiplayer.multiplayer_peer = peer
	
	var peer_id = multiplayer.get_unique_id()
	
	lobby_created.emit(lobby_id, peer_id)
	
	var start_message = "Host Session Started. Peer ID: %s Lobby ID: %s" % [peer_id, lobby_id]
	print(start_message)
	
func _on_lobby_joined(lobby_id: int, _permissions: int, _locked: bool, response: int) -> void:
	if response != Steam.ChatRoomEnterResponse.CHAT_ROOM_ENTER_RESPONSE_SUCCESS:
		var msg = "Failed to join lobby. Code: " + str(response)
		push_error(msg)
		connection_failed.emit(msg)
		return

	var host_id = Steam.getLobbyOwner(lobby_id)
	
	if host_id == Steam.getSteamID(): 
		print("Host cannot rejoin in his own room")
		return 

	peer = SteamMultiplayerPeer.new()
	var create_client_result = peer.create_client(host_id, 0)
	
	if create_client_result == OK:
		multiplayer.multiplayer_peer = peer
		var peer_id = multiplayer.get_unique_id()
		
		lobby_session_joined.emit(lobby_id, peer_id, host_id)
		
		var start_message = "Client Session Started. Connected to Host: %s . Peer ID: %s " % [host_id, peer_id]
		print(start_message)
	else:
		var msg = "Peer creation failed: " + str(create_client_result)
		push_error(msg)
		connection_failed.emit(msg)
