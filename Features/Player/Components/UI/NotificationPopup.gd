class_name NotificationPopup extends PanelContainer

signal response_received(accepted: bool)

@onready var label: Label = $VBoxContainer/Label
@onready var button_accept: Button = $VBoxContainer/HBoxContainer/ButtonAccept
@onready var button_reject: Button = $VBoxContainer/HBoxContainer/ButtonReject

func _ready() -> void:
	button_accept.pressed.connect(func(): _on_response(true))
	button_reject.pressed.connect(func(): _on_response(false))
	hide()

func request_decision(message: String) -> bool:
	label.text = message
	show()
	
	var result = await response_received
	
	hide()
	return result

func _on_response(accepted: bool):
	response_received.emit(accepted)
