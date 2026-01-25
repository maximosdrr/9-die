class_name InviteConfirmationMenu extends Control

@export var invite_menu: InviteMenu
@export var title_message = "Would you like to accept this invitation ?"
@export var accepted_message = "Match accepted... Waiting Start"
@export var rejected_message = "Match rejected"

@onready var show_time: Timer = $ShowTime
@onready var message: Label = $MessageBox/Message
@onready var confirm_button: Button = $ConfirmBox/ConfirmButton
@onready var decline_button: Button = $DeclineBox/DeclineButton

func _ready() -> void:
	self.visible = false
	show_time.timeout.connect(_on_show_time_is_over)
	invite_menu.invites_sent.connect(_on_invite_received)
	confirm_button.pressed.connect(_on_confirm_button_is_pressed)

func _on_invite_received(players: Array[String], selected_table: String):
	self.visible = true
	show_time.start()
	
func _on_show_time_is_over():
	self.visible = false
	message.text = title_message
	confirm_button.visible = true
	decline_button.visible = true

func _on_confirm_button_is_pressed():
	message.text = accepted_message
	confirm_button.visible = false
	decline_button.visible = false
	_send_confirmation_to_server.rpc(multiplayer.get_unique_id())
	
func _on_reject_button_is_pressed():
	message.text = rejected_message
	confirm_button.visible = false
	decline_button.visible = false

@rpc("any_peer", "call_local", "reliable")
func _send_confirmation_to_server(peer_id: int):
	if not multiplayer.is_server():
		return
	
	invite_menu.client_receive_confirmation.rpc(str(peer_id))
	
