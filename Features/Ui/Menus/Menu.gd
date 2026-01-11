class_name LobbyMenu extends Control

@export var game_level: Node3D # Reference to your level container
@export var lobby_container: VBoxContainer # Drag your VBox/ScrollContainer here

@onready var host_button: Button = $VBoxContainer/HBoxContainer/HostButton
@onready var refresh_button: Button = $VBoxContainer/HBoxContainer/RefreshListButton
@onready var join_session_local: Button = $VBoxContainer/HBoxContainer/JoinSessionLocal

func _ready() -> void:
	# Connect UI buttons
	host_button.pressed.connect(_on_host_pressed) 
	refresh_button.pressed.connect(_on_refresh_pressed)
	join_session_local.pressed.connect(_join_session_local)
	# Listen to the NetworkManager
	NetworkManager.lobby_list_received.connect(_update_lobby_list)
	NetworkManager.match_started.connect(_on_match_started)
	NetworkManager.connection_failed.connect(_on_error)

func _on_host_pressed() -> void:
	NetworkManager.create_host_session()
	# Optional: Disable buttons here to prevent double clicks

func _on_refresh_pressed() -> void:
	NetworkManager.refresh_lobby_list()

func _join_session_local() -> void:
	NetworkManager.join_session()

func _update_lobby_list(lobbies: Array) -> void:
	# Clear old list
	for child in lobby_container.get_children():
		child.queue_free()
	
	if lobbies.is_empty():
		return # You might want to show a "No lobbies found" label

	# Create new list
	for lobby_id in lobbies:
		var lobby_name = Steam.getLobbyData(lobby_id, "name")
		var num_members = Steam.getNumLobbyMembers(lobby_id)
		
		var btn = Button.new()
		btn.text = "%s (%s/4)" % [lobby_name, num_members]
		btn.pressed.connect(func(): NetworkManager.join_session(lobby_id))
		
		lobby_container.add_child(btn)

func _on_match_started() -> void:
	print("Match started! Switching view...")
	self.visible = false
	if game_level:
		# CHANGE THIS LINE: from .setup_game() to .start_match()
		game_level.start_match()

func _on_error(msg: String) -> void:
	print("Error: ", msg)
	# Ideally, show a PopupDialog here with the error message
