class_name LobbyMenu extends Control

@export var initial_level: Node3D

@onready var host_button: Button = $VBoxContainer/HBoxContainer/HostButton
@onready var refresh_button: Button = $VBoxContainer/HBoxContainer/RefreshListButton
@onready var join_session_local: Button = $VBoxContainer/HBoxContainer/JoinSessionLocal

func _ready() -> void:
	# Connect UI buttons
	host_button.pressed.connect(_on_host_pressed) 
	refresh_button.pressed.connect(_on_refresh_pressed)
	join_session_local.pressed.connect(_join_session_local)
	# Listen to the NetworkManager
	NetworkManager.network_provider.lobby_created.connect(_on_lobby_created)
	NetworkManager.network_provider.lobby_session_joined.connect(_on_lobby_session_joined)
	NetworkManager.network_provider.connection_failed.connect(_on_error)

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
