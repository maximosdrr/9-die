class_name InviteMenu extends Control

@export var invite_confirmation: InviteConfirmationMenu

@onready var player_list: VBoxContainer = $Inviting/PlayersContainer/List
@onready var table_list: VBoxContainer = $Inviting/TablesContainer/List
@onready var send_invites: Button = $Inviting/Actions/SendInvites
@onready var start_match: Control = $StartMatch
@onready var inviting: Control = $Inviting
@onready var start_match_button: Button = $StartMatch/StartMatch/StartMatchButton
@onready var start_match_timer: Timer = $StartMatch/StartMatchTimer
@onready var accepted_players_placeholder: Label = $StartMatch/PlayersPlaceholder/Label

var selected_players: Array[String] = []
var selected_table = null

var players_confirmed: Array[String] = []

signal invites_sent(players: Array[String], selected_table: String)

func _ready() -> void:
	self.visible = false
	inviting.visible = true
	start_match.visible = false
	
	send_invites.pressed.connect(_on_send_button_pressed)
	start_match_timer.timeout.connect(_on_start_match_timer_ends)
	start_match_button.pressed.connect(_on_match_start_button_pressed)

func _unhandled_input(_event: InputEvent) -> void:
	if Input.is_action_just_pressed("invite") and not start_match.visible:
		if not visible:
			Input.set_mouse_mode(Input.MOUSE_MODE_VISIBLE)
			visible = true
			_update_players_container()
			_update_table_list()
		else:
			Input.set_mouse_mode(Input.MOUSE_MODE_CAPTURED)
			visible = false

func _update_players_container():
	var available_players = PlayerRegistry.get_all_players()
	
	for node in player_list.get_children():
		node.queue_free()
	
	for player in available_players:
		if player.name == str(multiplayer.get_unique_id()): continue
		
		var label = Label.new()
		label.text = player.name
		var check_box = CheckBox.new()
		check_box.pressed.connect(_on_select_player.bind(player.name))
		player_list.add_child(label)
		player_list.add_child(check_box)

func _update_table_list():
	var tables = get_tree().get_nodes_in_group(Groups.TABLE)
	
	for node in table_list.get_children():
		node.queue_free()
	
	for table in tables:
		var label = Label.new()
		label.text = "Table %s" % table.name
		
		var check_box = CheckBox.new()
		check_box.pressed.connect(_on_select_table.bind(table.name, check_box))
		
		table_list.add_child(label)
		table_list.add_child(check_box)

func _on_select_player(player_name: String):
	if selected_players.has(player_name): 
		selected_players.erase(player_name)
	else:
		selected_players.append(player_name)

func _on_select_table(table_name: String, check_box: CheckBox):
	if table_name == selected_table:
		selected_table = null
		check_box.set_pressed_no_signal(false)
		return

	_reset_table_checkboxes()
	
	check_box.set_pressed_no_signal(true)
	selected_table = table_name

func _reset_table_checkboxes():
	for node in table_list.get_children():
		if node is CheckBox:
			node.set_pressed_no_signal(false)

func _reset_players_checkboxes():
	for node in player_list.get_children():
		if node is CheckBox:
			node.set_pressed_no_signal(false)
		
func _on_send_button_pressed():
	if selected_table == null: return
	#if selected_players.size() == 0: return

	_server_distribute_invites.rpc_id(
		MultiplayerPeer.TARGET_PEER_SERVER,
	 	selected_players, 
		selected_table
	)
	
	inviting.visible = false
	start_match.visible = true
	start_match_timer.start()

@rpc('any_peer', 'call_local', 'reliable') 
func _server_distribute_invites(_players: Array[String], _selected_table: String):
	if not multiplayer.is_server():
		return
	
	var sender_id = multiplayer.get_remote_sender_id()
	
	_client_receive_invite.rpc(
		_players,
		_selected_table, 
		sender_id
	)

@rpc('authority', 'call_local', 'reliable')
func _client_receive_invite(_players: Array[String], _selected_table: String, sender_id: int):
	var client_id = multiplayer.get_unique_id()
	
	if sender_id == client_id:
		return
	
	if not _players.has(str(client_id)):
		return

	invites_sent.emit(_players, _selected_table)

@rpc("authority", "call_local", "reliable")
func client_receive_confirmation(player_id: String):
	players_confirmed.append(player_id)
	accepted_players_placeholder.text = ", ".join(PackedStringArray(players_confirmed))

func _on_start_match_timer_ends():
	players_confirmed = []
	selected_table = null
	selected_players = []
	
	_reset_players_checkboxes()
	_reset_table_checkboxes()
	
	inviting.visible = true
	start_match.visible = false
	self.visible = false
	
	accepted_players_placeholder.text = "No one have accepted your invitation yet!"

func _on_match_start_button_pressed():
	var tables = get_tree().get_nodes_in_group(Groups.TABLE)
	var target_table = null
	
	for table in tables:
		if table.name != selected_table: continue
		target_table = table
	
	if target_table == null:
		push_error("Table not found!")
	
	if target_table is Table:
		players_confirmed.append(str(multiplayer.get_unique_id()))
		target_table.start_match(players_confirmed)
		start_match_timer.stop()
		_on_start_match_timer_ends()
