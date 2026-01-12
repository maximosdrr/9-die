class_name WaitingGameStart extends State

@export var table_influence: Area3D
@export var start_game_ui: PoolStartGameUI

var players_on_influency_area: Array[Node3D] = []

func _init() -> void:
	self.type = State.Type.WAITING_GAME_START

func enter(_metadata: Dictionary[Variant, Variant]):
	start_game_ui.hide()
	_connect_signals()

func exit(_metadata: Dictionary[Variant, Variant]):
	_disconnect_signals()

func process(_delta: float) -> void:
	if Input.is_action_just_pressed("start_game"):
		if players_on_influency_area.size() >= 1:
			var metadata = {}
			var players_ids = players_on_influency_area.map(
				func (player) : return player.name
			)
			metadata.set("players_ids", players_ids)
			state_machine.change_state(State.Type.GAME_STARTING, metadata)

func _on_body_enter_in_influence_area(_body: Node3D):
	var bodies = table_influence.get_overlapping_bodies()
	
	players_on_influency_area = bodies.filter(
		func(body): return body is Player
	)
	
	_update_ui_text()

	if players_on_influency_area.size() > 0:
		start_game_ui.show()

func _on_body_exited_in_influence_area(_body: Node3D):
	var bodies = table_influence.get_overlapping_bodies()
	
	players_on_influency_area = bodies.filter(
		func(body): return body is Player
	)
	
	_update_ui_text()
	
	if players_on_influency_area.size() == 0:
		start_game_ui.hide()

func _update_ui_text():
	var total_players = players_on_influency_area.size()
	var text = "Waiting Start (Press F)\nPlayers %s/4" % [total_players]
	start_game_ui.set_text(text)

func _connect_signals():
	if not table_influence.body_entered.is_connected(_on_body_enter_in_influence_area):
		table_influence.body_entered.connect(_on_body_enter_in_influence_area)
	if not table_influence.body_exited.is_connected(_on_body_exited_in_influence_area):
		table_influence.body_exited.connect(_on_body_exited_in_influence_area)

func _disconnect_signals():
	if table_influence.body_entered.is_connected(_on_body_enter_in_influence_area):
		table_influence.body_entered.disconnect(_on_body_enter_in_influence_area)
		
	if table_influence.body_exited.is_connected(_on_body_exited_in_influence_area):
		table_influence.body_exited.disconnect(_on_body_exited_in_influence_area)
