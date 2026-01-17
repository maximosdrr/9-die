class_name LobbyMenu extends Control

@export var initial_level: Node3D

@onready var host_button: Button = $VBoxContainer/HBoxContainer/HostButton
@onready var refresh_button: Button = $VBoxContainer/HBoxContainer/RefreshListButton
@onready var join_session_local: Button = $VBoxContainer/HBoxContainer/JoinSessionLocal
@onready var lobby_list_container: Control = $VBoxContainer/HBoxContainer/ScrollContainer/LobbyList
func _ready() -> void:
	host_button.pressed.connect(_on_host_pressed) 
	refresh_button.pressed.connect(_on_refresh_pressed)
	join_session_local.pressed.connect(_join_session_local)
	
	NetworkManager.network_provider.lobby_created.connect(_on_lobby_created)
	NetworkManager.network_provider.lobby_session_joined.connect(_on_lobby_session_joined)
	NetworkManager.network_provider.connection_failed.connect(_on_error)
	NetworkManager.network_provider.lobby_list_received.connect(_on_lobby_list_received)

func _on_host_pressed() -> void:
	NetworkManager.create_host_session()
	host_button.disabled = true
	
func _on_refresh_pressed() -> void:
	NetworkManager.refresh_lobby_list()

func _join_session_local() -> void:
	NetworkManager.join_session()
	join_session_local.disabled = true
	
func _on_lobby_session_joined(_lobby_id: int, _guest_peer_id: int, _host_id: int):
	assert(initial_level != null)
	await NetworkManager.network_provider.player_connected
	self.visible = false

func _on_lobby_created(_lobby_id: int, _host_peer_id: int) -> void:
	assert(initial_level != null)
	await NetworkManager.network_provider.player_connected
	self.visible = false

func _on_error(msg: String) -> void:
	print("Error: ", msg)

func _on_lobby_list_received(lobbies: Array) -> void:
	for child in lobby_list_container.get_children():
		child.queue_free()

	for lobby in lobbies:
		var lobby_id: int = 0
		var lobby_name: String = "Unknown Lobby"
		
		if typeof(lobby) == TYPE_INT:
			lobby_id = lobby
			lobby_name = "Room " + str(lobby_id)
			
		elif typeof(lobby) == TYPE_DICTIONARY:
			lobby_id = lobby.get("id", 0)
			lobby_name = lobby.get("name", "Room")

		var btn = Button.new()
		btn.text = "%s (ID: %s)" % [lobby_name, str(lobby_id)]
		btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
		
		btn.pressed.connect(_on_lobby_item_pressed.bind(lobby_id))
		lobby_list_container.add_child(btn)

func _on_lobby_item_pressed(lobby_id: int) -> void:
	print("Trying enter in lobby: ", lobby_id)
	NetworkManager.join_session(lobby_id)
	self.visible = false
